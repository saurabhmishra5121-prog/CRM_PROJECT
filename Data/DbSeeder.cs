using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AcxiomCRM.Data;

public static class DbSeeder
{
    public static readonly string[] Roles = { "Admin", "Manager", "SalesExecutive" };

    public static async Task SeedAsync(IServiceProvider sp, IConfiguration configuration)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in Roles)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        await CreateConfiguredUserAsync(userManager, configuration, "Admin", "System Admin");
        await CreateConfiguredUserAsync(userManager, configuration, "Manager", "Sales Manager");
    }

    private static async Task CreateConfiguredUserAsync(UserManager<ApplicationUser> userManager,
        IConfiguration configuration, string role, string fullName)
    {
        var section = configuration.GetSection($"BootstrapUsers:{role}");
        var email = section["Email"];
        var password = section["Password"];

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            return;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                $"Both BootstrapUsers:{role}:Email and BootstrapUsers:{role}:Password must be configured.");

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true,
                IsActive = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to create the configured {role} account: " +
                    string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to assign the {role} role: " +
                    string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }
    }

}