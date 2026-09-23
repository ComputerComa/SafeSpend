using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Identity;

public static class IdentityDatabaseInitializer
{
    public static async Task InitializeAsync(
        SafeSpendIdentityDbContext context)
    {
        await context.Database.EnsureCreatedAsync();
    }
}
