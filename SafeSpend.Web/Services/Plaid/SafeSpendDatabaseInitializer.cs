using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Plaid;

public static class SafeSpendDatabaseInitializer
{
    public static async Task InitializeAsync(
        SafeSpendDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        // The app originally created only the Plaid tables with EnsureCreated.
        // Keep existing local databases usable while the first real migration
        // is introduced.
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PaycheckSchedules" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_PaycheckSchedules" PRIMARY KEY,
                "NextPaycheckDate" TEXT NOT NULL,
                "FollowingPaycheckDate" TEXT NOT NULL,
                "AmountCents" INTEGER NOT NULL,
                "CushionCents" INTEGER NOT NULL
            );
            """);

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BillSchedules" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BillSchedules" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "NextDueDate" TEXT NOT NULL,
                "AmountCents" INTEGER NOT NULL,
                "Frequency" INTEGER NOT NULL
            );
            """);
    }
}
