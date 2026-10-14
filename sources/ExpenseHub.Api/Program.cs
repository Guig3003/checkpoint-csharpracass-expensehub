using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Domain;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseHub.Api;

public static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=expensehub.db";
        string jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jwtKey)) jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Configuration["Jwt:Key"] = jwtKey;
        builder.Services.AddDbContext<ExpenseHubDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddIdentityCore<ApplicationUser>(options => { options.User.RequireUniqueEmail = true; options.Password.RequiredLength = 8; options.Password.RequireDigit = true; options.Password.RequireNonAlphanumeric = false; }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ExpenseHubDbContext>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ExpenseHub", ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ExpenseHub.Client", ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateLifetime = true });
        builder.Services.AddAuthorization(); builder.Services.AddScoped<ExpenseService>(); builder.Services.AddOpenApi();
        WebApplication app = builder.Build();
        using (IServiceScope scope = app.Services.CreateScope()) { ExpenseHubDbContext db = scope.ServiceProvider.GetRequiredService<ExpenseHubDbContext>(); await db.Database.EnsureCreatedAsync(); await SeedData.InitializeAsync(scope.ServiceProvider); }
        app.UseAuthentication(); app.UseAuthorization(); if (app.Environment.IsDevelopment()) app.MapOpenApi();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithName("GetHealth");
        app.MapPost("/register", async (RegisterRequest request, UserManager<ApplicationUser> users) => { if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password)) return Results.BadRequest("Email e senha são obrigatórios."); ApplicationUser user = new() { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName ?? request.Email }; IdentityResult result = await users.CreateAsync(user, request.Password); return result.Succeeded ? Results.Created($"/api/admin/users/{user.Id}", new { user.Id, user.Email }) : Results.BadRequest(result.Errors.Select(e => e.Description)); }).AllowAnonymous();
        app.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users, IConfiguration config) => { ApplicationUser? user = await users.FindByEmailAsync(request.Email); if (user is null || !await users.CheckPasswordAsync(user, request.Password)) return Results.Unauthorized(); IList<string> roles = await users.GetRolesAsync(user); List<Claim> claims = [new(JwtRegisteredClaimNames.Sub, user.Id), new(ClaimTypes.NameIdentifier, user.Id), new(ClaimTypes.Email, user.Email ?? user.UserName ?? "")]; claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role))); SymmetricSecurityKey securityKey = new(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)); JwtSecurityToken token = new(config["Jwt:Issuer"] ?? "ExpenseHub", config["Jwt:Audience"] ?? "ExpenseHub.Client", claims, DateTime.UtcNow, DateTime.UtcNow.AddHours(4), new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256)); return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), userId = user.Id, roles }); }).AllowAnonymous();
        app.MapGet("/api/admin/users", (UserManager<ApplicationUser> users) => Results.Ok(users.Users.Select(u => new { u.Id, u.Email, u.DisplayName }))).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        app.MapPut("/api/admin/users/{id}/roles", async (string id, RolesRequest request, UserManager<ApplicationUser> users, ClaimsPrincipal principal) => { ApplicationUser? user = await users.FindByIdAsync(id); if (user is null) return Results.NotFound(); string[] valid = [Roles.Admin, Roles.Employee, Roles.Approver, Roles.Finance, Roles.Auditor]; if (request.Roles.Any(role => !valid.Contains(role, StringComparer.OrdinalIgnoreCase))) return Results.BadRequest("Role desconhecida."); if (id == principal.FindFirstValue(ClaimTypes.NameIdentifier) && !request.Roles.Contains(Roles.Admin, StringComparer.OrdinalIgnoreCase)) return Results.Conflict("Não é possível remover a própria role Admin."); IList<string> current = await users.GetRolesAsync(user); await users.RemoveFromRolesAsync(user, current); await users.AddToRolesAsync(user, request.Roles.Distinct(StringComparer.OrdinalIgnoreCase)); return Results.NoContent(); }).RequireAuthorization(p => p.RequireRole(Roles.Admin));
        RouteGroupBuilder expenses = app.MapGroup("/api/expenses").RequireAuthorization();
        expenses.MapPost("/", async (CreateExpenseRequest request, ClaimsPrincipal user, ExpenseService service) => { if (!ExpenseRules.IsValid(request.Description, request.Amount, request.ExpenseDate)) return Results.BadRequest(new { error = "Dados do reembolso inválidos." }); Expense expense = await service.CreateAsync(user, request); return Results.Created($"/api/expenses/{expense.Id}", expense); }).RequireAuthorization(p => p.RequireRole(Roles.Employee));
        expenses.MapPut("/{id:int}", (int id, UpdateExpenseRequest request, ClaimsPrincipal user, ExpenseService service) => service.EditAsync(id, user, request)).RequireAuthorization(p => p.RequireRole(Roles.Employee)); expenses.MapGet("/", (ClaimsPrincipal user, ExpenseService service) => service.ListAsync(user)).RequireAuthorization(p => p.RequireRole(Roles.Employee, Roles.Approver, Roles.Finance, Roles.Auditor)); expenses.MapGet("/{id:int}", (int id, ClaimsPrincipal user, ExpenseService service) => service.GetAsync(id, user)).RequireAuthorization(p => p.RequireRole(Roles.Employee, Roles.Approver, Roles.Finance, Roles.Auditor)); expenses.MapPost("/{id:int}/submit", (int id, ClaimsPrincipal user, ExpenseService service) => service.SubmitAsync(id, user)).RequireAuthorization(p => p.RequireRole(Roles.Employee)); expenses.MapPost("/{id:int}/approve", (int id, ClaimsPrincipal user, ExpenseService service) => service.ApproveAsync(id, user)).RequireAuthorization(p => p.RequireRole(Roles.Approver)); expenses.MapPost("/{id:int}/reject", (int id, RejectRequest request, ClaimsPrincipal user, ExpenseService service) => service.RejectAsync(id, user, request)).RequireAuthorization(p => p.RequireRole(Roles.Approver)); expenses.MapPost("/{id:int}/pay", (int id, ClaimsPrincipal user, ExpenseService service) => service.PayAsync(id, user)).RequireAuthorization(p => p.RequireRole(Roles.Finance)); expenses.MapGet("/{id:int}/history", (int id, ClaimsPrincipal user, ExpenseService service) => service.HistoryAsync(id, user)).RequireAuthorization(p => p.RequireRole(Roles.Employee, Roles.Approver, Roles.Finance, Roles.Auditor));
        await app.RunAsync();
    }
    public sealed record RegisterRequest(string Email, string Password, string? DisplayName); public sealed record LoginRequest(string Email, string Password); public sealed record RolesRequest(string[] Roles); public sealed record CreateExpenseRequest(string Description, decimal Amount, DateTime ExpenseDate, int CategoryId = 1); public sealed record UpdateExpenseRequest(string Description, decimal Amount, DateTime ExpenseDate, int CategoryId = 1); public sealed record RejectRequest(string Justification);
}
