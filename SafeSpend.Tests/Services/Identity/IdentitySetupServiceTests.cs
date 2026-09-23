using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SafeSpend.Web.Services.Identity;

namespace SafeSpend.Tests.Services.Identity;

public sealed class IdentitySetupServiceTests
{
    [Fact]
    public async Task CreatesFirstAdministratorAndClosesSetup()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-identity-{Guid.NewGuid():N}.db");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<SafeSpendIdentityDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            services.AddIdentityCore<ApplicationUser>(options =>
                options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<SafeSpendIdentityDbContext>()
                .AddDefaultTokenProviders();
            services.AddScoped<IdentitySetupService>();

            await using var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                var context = scope.ServiceProvider
                    .GetRequiredService<SafeSpendIdentityDbContext>();
                await context.Database.EnsureCreatedAsync();

                var setupService = scope.ServiceProvider
                    .GetRequiredService<IdentitySetupService>();

                Assert.True(await setupService.IsSetupRequiredAsync());

                var result = await setupService
                    .CreateFirstAdministratorAsync(
                        "admin@example.test",
                        "SafeSpend Admin",
                        "Strong-password-123!");

                Assert.True(result.Succeeded);
                Assert.False(await setupService.IsSetupRequiredAsync());

                var userManager = scope.ServiceProvider
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager
                    .FindByEmailAsync("admin@example.test");

                Assert.NotNull(user);
                Assert.Equal("SafeSpend Admin", user.DisplayName);
                Assert.True(await userManager.IsInRoleAsync(
                    user,
                    SafeSpend.Web.Services.Identity.IdentityConstants
                        .AdministratorRole));

                var secondAttempt = await setupService
                    .CreateFirstAdministratorAsync(
                        "second@example.test",
                        null,
                        "Strong-password-123!");

                Assert.False(secondAttempt.Succeeded);
                Assert.Equal(
                    "SetupComplete",
                    Assert.Single(secondAttempt.Errors).Code);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
