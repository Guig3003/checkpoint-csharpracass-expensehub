using Microsoft.AspNetCore.Identity;
namespace ExpenseHub.Api.Domain;

public static class Roles { public const string Admin = "Admin"; public const string Employee = "Employee"; public const string Approver = "Approver"; public const string Finance = "Finance"; public const string Auditor = "Auditor"; }
public enum ExpenseStatus { Draft, Submitted, Approved, Rejected, Paid }
public sealed class ApplicationUser : IdentityUser { public string DisplayName { get; set; } = ""; }
public sealed class Expense { public int Id { get; set; } public string OwnerId { get; set; } = ""; public ApplicationUser? Owner { get; set; } public string Description { get; set; } = ""; public decimal Amount { get; set; } public DateTime ExpenseDate { get; set; } public int CategoryId { get; set; } public ExpenseCategory? Category { get; set; } public ExpenseStatus Status { get; set; } public DateTime CreatedAtUtc { get; set; } }
public sealed class ExpenseCategory { public int Id { get; set; } public string Name { get; set; } = ""; public ICollection<Expense> Expenses { get; set; } = []; }
public sealed class ExpenseHistory { public int Id { get; set; } public int ExpenseId { get; set; } public Expense? Expense { get; set; } public string Action { get; set; } = ""; public string ActorId { get; set; } = ""; public DateTime OccurredAtUtc { get; set; } public ExpenseStatus? FromStatus { get; set; } public ExpenseStatus ToStatus { get; set; } public string? Justification { get; set; } public string? Changes { get; set; } }
public sealed class PaymentRecord { public int Id { get; set; } public int ExpenseId { get; set; } public string PaidById { get; set; } = ""; public DateTime PaidAtUtc { get; set; } public string TransactionReference { get; set; } = ""; }
