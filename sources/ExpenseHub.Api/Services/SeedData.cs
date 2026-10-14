using ExpenseHub.Api.Domain;
using Microsoft.AspNetCore.Identity;
namespace ExpenseHub.Api.Services;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        RoleManager<IdentityRole> roles = services.GetRequiredService<RoleManager<IdentityRole>>(); UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (string role in new[] { Roles.Admin, Roles.Employee, Roles.Approver, Roles.Finance, Roles.Auditor }) if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
        string? email = Environment.GetEnvironmentVariable("EXPENSEHUB_ADMIN_EMAIL"); string? password = Environment.GetEnvironmentVariable("EXPENSEHUB_ADMIN_PASSWORD"); if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        ApplicationUser? admin = await users.FindByEmailAsync(email); if (admin is null) { admin = new ApplicationUser { UserName = email, Email = email, DisplayName = "Administrator" }; await users.CreateAsync(admin, password); }
        if (!await users.IsInRoleAsync(admin, Roles.Admin)) await users.AddToRoleAsync(admin, Roles.Admin);
    }
}
