using System.Security.Claims;
using System.Text.Json;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseHub.Api.Services;

public sealed class ExpenseService(ExpenseHubDbContext db)
{
    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private static bool Has(ClaimsPrincipal user, string role) => user.IsInRole(role);
    private static IResult Bad(string message) => Results.BadRequest(new { error = message });
    private async Task<Expense?> FindVisibleAsync(int id, ClaimsPrincipal user)
    {
        IQueryable<Expense> query = db.Expenses.AsNoTracking(); string uid = UserId(user); bool employee = Has(user, Roles.Employee); bool approver = Has(user, Roles.Approver); bool finance = Has(user, Roles.Finance);
        if (Has(user, Roles.Auditor)) return await query.FirstOrDefaultAsync(e => e.Id == id);
        return await query.FirstOrDefaultAsync(e => e.Id == id &&
            (employee && e.OwnerId == uid ||
             approver && e.Status == ExpenseStatus.Submitted && e.OwnerId != uid ||
             finance && (e.Status == ExpenseStatus.Approved || e.Status == ExpenseStatus.Paid) && e.OwnerId != uid));
    }
    public async Task<Expense> CreateAsync(ClaimsPrincipal user, Program.CreateExpenseRequest request)
    {
        if (!ExpenseRules.IsValid(request.Description, request.Amount, request.ExpenseDate)) throw new ArgumentException("Dados do reembolso inválidos.");
        Expense expense = new() { OwnerId = UserId(user), Description = request.Description.Trim(), Amount = request.Amount, ExpenseDate = request.ExpenseDate, CategoryId = request.CategoryId, Status = ExpenseStatus.Draft, CreatedAtUtc = DateTime.UtcNow };
        db.Expenses.Add(expense); db.ExpenseHistories.Add(new ExpenseHistory { Expense = expense, Action = "Created", ActorId = expense.OwnerId, OccurredAtUtc = DateTime.UtcNow, ToStatus = ExpenseStatus.Draft }); await db.SaveChangesAsync(); return expense;
    }
    public async Task<IResult> EditAsync(int id, ClaimsPrincipal user, Program.UpdateExpenseRequest request)
    {
        Expense? expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id); if (expense is null || expense.OwnerId != UserId(user)) return Results.NotFound(); if (expense.Status != ExpenseStatus.Draft) return Results.Conflict("Somente rascunhos podem ser editados."); if (!ExpenseRules.IsValid(request.Description, request.Amount, request.ExpenseDate)) return Bad("Dados do reembolso inválidos.");
        string changes = JsonSerializer.Serialize(new { oldDescription = expense.Description, oldAmount = expense.Amount, oldExpenseDate = expense.ExpenseDate, newDescription = request.Description, newAmount = request.Amount, newExpenseDate = request.ExpenseDate }); expense.Description = request.Description.Trim(); expense.Amount = request.Amount; expense.ExpenseDate = request.ExpenseDate; expense.CategoryId = request.CategoryId; db.ExpenseHistories.Add(new ExpenseHistory { ExpenseId = id, Action = "Edited", ActorId = UserId(user), OccurredAtUtc = DateTime.UtcNow, ToStatus = expense.Status, Changes = changes }); await db.SaveChangesAsync(); return Results.Ok(expense);
    }
    public async Task<IEnumerable<Expense>> ListAsync(ClaimsPrincipal user)
    {
        string uid = UserId(user); bool employee = Has(user, Roles.Employee); bool approver = Has(user, Roles.Approver); bool finance = Has(user, Roles.Finance); IQueryable<Expense> q = db.Expenses.AsNoTracking(); if (Has(user, Roles.Auditor)) return await q.ToListAsync(); return await q.Where(e => employee && e.OwnerId == uid || approver && e.Status == ExpenseStatus.Submitted && e.OwnerId != uid || finance && (e.Status == ExpenseStatus.Approved || e.Status == ExpenseStatus.Paid) && e.OwnerId != uid).ToListAsync();
    }
    public async Task<IResult> GetAsync(int id, ClaimsPrincipal user) => await FindVisibleAsync(id, user) is Expense expense ? Results.Ok(expense) : Results.NotFound();
    public async Task<IResult> SubmitAsync(int id, ClaimsPrincipal user)
    {
        if (!Has(user, Roles.Employee)) return Results.Forbid();
        Expense? expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id);
        if (expense is null || expense.OwnerId != UserId(user)) return Results.NotFound();
        if (!ExpenseRules.CanTransition(expense.Status, ExpenseStatus.Submitted)) return Results.Conflict("Only drafts can be submitted.");
        expense.Status = ExpenseStatus.Submitted;
        db.ExpenseHistories.Add(new ExpenseHistory { ExpenseId = id, Action = "Submitted", ActorId = UserId(user), OccurredAtUtc = DateTime.UtcNow, FromStatus = ExpenseStatus.Draft, ToStatus = ExpenseStatus.Submitted });
        await db.SaveChangesAsync();
        return Results.Ok(expense);
    }
    public Task<IResult> ApproveAsync(int id, ClaimsPrincipal user) => TransitionAsync(id, user, Roles.Approver, ExpenseStatus.Submitted, ExpenseStatus.Approved, "Approved");
    public async Task<IResult> RejectAsync(int id, ClaimsPrincipal user, Program.RejectRequest request)
    { if (!ExpenseRules.IsValidJustification(request.Justification)) return Bad("Justificativa deve ter entre 10 e 500 caracteres."); return await TransitionAsync(id, user, Roles.Approver, ExpenseStatus.Submitted, ExpenseStatus.Rejected, "Rejected", request.Justification.Trim()); }
    public Task<IResult> PayAsync(int id, ClaimsPrincipal user) => TransitionAsync(id, user, Roles.Finance, ExpenseStatus.Approved, ExpenseStatus.Paid, "Paid");
    private async Task<IResult> TransitionAsync(int id, ClaimsPrincipal user, string role, ExpenseStatus from, ExpenseStatus to, string action, string? justification = null)
    {
        if (!Has(user, role)) return Results.Forbid(); Expense? expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id); if (expense is null) return Results.NotFound(); if (expense.Status != from) return Results.Conflict("Transição incompatível com o estado atual."); if (expense.OwnerId == UserId(user)) return Results.Forbid(); expense.Status = to; db.ExpenseHistories.Add(new ExpenseHistory { ExpenseId = id, Action = action, ActorId = UserId(user), OccurredAtUtc = DateTime.UtcNow, FromStatus = from, ToStatus = to, Justification = justification }); if (to == ExpenseStatus.Paid) db.PaymentRecords.Add(new PaymentRecord { ExpenseId = id, PaidById = UserId(user), PaidAtUtc = DateTime.UtcNow, TransactionReference = $"PAY-{Guid.NewGuid():N}" }); await db.SaveChangesAsync(); return Results.Ok(expense);
    }
    public async Task<IResult> HistoryAsync(int id, ClaimsPrincipal user) => await FindVisibleAsync(id, user) is null ? Results.NotFound() : Results.Ok(await db.ExpenseHistories.AsNoTracking().Where(h => h.ExpenseId == id).OrderBy(h => h.OccurredAtUtc).ToListAsync());
}
