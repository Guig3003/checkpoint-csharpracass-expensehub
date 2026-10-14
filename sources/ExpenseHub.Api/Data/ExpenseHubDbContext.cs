using ExpenseHub.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace ExpenseHub.Api.Data;

public sealed class ExpenseHubDbContext(DbContextOptions<ExpenseHubDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Expense> Expenses => Set<Expense>(); public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>(); public DbSet<ExpenseHistory> ExpenseHistories => Set<ExpenseHistory>(); public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    protected override void OnModelCreating(ModelBuilder builder) { base.OnModelCreating(builder); builder.Entity<Expense>().Property(e => e.Amount).HasPrecision(18, 2); builder.Entity<Expense>().HasOne(e => e.Owner).WithMany().HasForeignKey(e => e.OwnerId).OnDelete(DeleteBehavior.Restrict); builder.Entity<ExpenseCategory>().HasData(new ExpenseCategory { Id = 1, Name = "Geral" }); }
}
