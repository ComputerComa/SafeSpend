using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Plaid;

public static class SafeSpendDatabaseInitializer
{
    public static async Task InitializeAsync(
        SafeSpendDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PlaidConnections" (
                "UserId" TEXT NOT NULL CONSTRAINT "PK_PlaidConnections" PRIMARY KEY,
                "ItemId" TEXT NOT NULL,
                "ProtectedAccessToken" TEXT NOT NULL,
                "TransactionCursor" TEXT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Connected',
                "LastWebhookCode" TEXT NULL,
                "LastWebhookAt" TEXT NULL
            );
            """);

        await AddColumnIfMissingAsync(
            context,
            "PlaidConnections",
            "Status");
        await AddColumnIfMissingAsync(
            context,
            "PlaidConnections",
            "LastWebhookCode");
        await AddColumnIfMissingAsync(
            context,
            "PlaidConnections",
            "LastWebhookAt");

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlaidConnections_ItemId"
            ON "PlaidConnections" ("ItemId");
            """);

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

    private static async Task AddColumnIfMissingAsync(
        SafeSpendDbContext context,
        string tableName,
        string columnName)
    {
        await using var command = context.Database.GetDbConnection()
            .CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        if (command.Connection!.State !=
            System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(
                    reader.GetString(1),
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        var alterStatement = columnName switch
        {
            "Status" =>
                "ALTER TABLE \"PlaidConnections\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'Connected';",
            "LastWebhookCode" =>
                "ALTER TABLE \"PlaidConnections\" ADD COLUMN \"LastWebhookCode\" TEXT NULL;",
            "LastWebhookAt" =>
                "ALTER TABLE \"PlaidConnections\" ADD COLUMN \"LastWebhookAt\" TEXT NULL;",
            _ => throw new InvalidOperationException(
                "Unexpected SafeSpend database column.")
        };

        await context.Database.ExecuteSqlRawAsync(alterStatement);
    }
}
