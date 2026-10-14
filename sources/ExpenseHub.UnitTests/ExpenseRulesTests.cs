using ExpenseHub.Api.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ExpenseHub.UnitTests;

[TestClass]
public sealed class ExpenseRulesTests
{
    [TestMethod]
    public void DescriptionMustHaveTenCharacters() => Assert.IsFalse(ExpenseRules.IsValid("curta", 10m, DateTime.UtcNow));

    [TestMethod]
    public void AmountMustBePositiveAndWithinIntegerLimit()
    {
        Assert.IsFalse(ExpenseRules.IsValid("descrição suficientemente longa", 0m, DateTime.UtcNow));
        Assert.IsFalse(ExpenseRules.IsValid("descrição suficientemente longa", int.MaxValue + 1m, DateTime.UtcNow));
    }

    [TestMethod]
    public void ExpenseDateCannotBeFuture() => Assert.IsFalse(ExpenseRules.IsValid("descrição suficientemente longa", 10m, DateTime.UtcNow.AddMinutes(1)));

    [TestMethod]
    public void RejectionRequiresJustification() => Assert.IsFalse(ExpenseRules.IsValidJustification("motivo"));

    [TestMethod]
    public void OnlyExpectedTransitionsAreAllowed()
    {
        Assert.IsTrue(ExpenseRules.CanTransition(ExpenseStatus.Draft, ExpenseStatus.Submitted));
        Assert.IsTrue(ExpenseRules.CanTransition(ExpenseStatus.Submitted, ExpenseStatus.Approved));
        Assert.IsFalse(ExpenseRules.CanTransition(ExpenseStatus.Paid, ExpenseStatus.Draft));
        Assert.IsFalse(ExpenseRules.CanTransition(ExpenseStatus.Rejected, ExpenseStatus.Submitted));
    }
}
