using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Identity;

public sealed class IdentitySetupService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager)
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);

    public async Task<bool> IsSetupRequiredAsync()
    {
        return !await userManager.Users.AnyAsync();
    }

    public async Task<IdentityResult> CreateFirstAdministratorAsync(
        string email,
        string? displayName,
        string password)
    {
        await SetupLock.WaitAsync();

        try
        {
            if (!await IsSetupRequiredAsync())
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "SetupComplete",
                    Description = "Administrator setup has already been completed."
                });
            }

            if (!await roleManager.RoleExistsAsync(
                    IdentityConstants.AdministratorRole))
            {
                var roleResult = await roleManager.CreateAsync(
                    new IdentityRole(IdentityConstants.AdministratorRole));

                if (!roleResult.Succeeded)
                {
                    return roleResult;
                }
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? null
                    : displayName.Trim()
            };

            var userResult = await userManager.CreateAsync(user, password);
            if (!userResult.Succeeded)
            {
                return userResult;
            }

            var roleAssignmentResult = await userManager.AddToRoleAsync(
                user,
                IdentityConstants.AdministratorRole);

            if (!roleAssignmentResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
            }

            return roleAssignmentResult;
        }
        finally
        {
            SetupLock.Release();
        }
    }
}
