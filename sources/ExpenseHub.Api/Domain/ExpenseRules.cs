namespace ExpenseHub.Api.Domain;

public static class ExpenseRules
{
    public static bool IsValid(string description, decimal amount, DateTime date) => description.Trim().Length is >= 10 and <= 500 && amount >= 0.01m && amount <= int.MaxValue && date <= DateTime.UtcNow;
    public static bool IsValidJustification(string justification) => justification.Trim().Length is >= 10 and <= 500;
    public static bool CanTransition(ExpenseStatus current, ExpenseStatus next) => (current, next) switch { (ExpenseStatus.Draft, ExpenseStatus.Submitted) => true, (ExpenseStatus.Submitted, ExpenseStatus.Approved) => true, (ExpenseStatus.Submitted, ExpenseStatus.Rejected) => true, (ExpenseStatus.Approved, ExpenseStatus.Paid) => true, _ => false };
}
