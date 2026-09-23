using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SafeSpend.Web.Data;

internal static class LegacyDatabaseMigrationAdopter
{
    public static async Task MigrateAsync(
        DbContext context,
        string initialMigrationId,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>
            requiredSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialMigrationId);
        ArgumentNullException.ThrowIfNull(requiredSchema);

        var availableMigrations = context.Database.GetMigrations();
        if (!availableMigrations.Contains(
                initialMigrationId,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The initial EF Core migration '{initialMigrationId}' " +
                "was not found.");
        }

        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync(cancellationToken);
        if (appliedMigrations.Any())
        {
            await context.Database.MigrateAsync(cancellationToken);
            return;
        }

        var existingTables = await GetExistingTablesAsync(
            context,
            cancellationToken);
        if (existingTables.Count == 0)
        {
            await context.Database.MigrateAsync(cancellationToken);
            return;
        }

        var schemaProblems = await FindSchemaProblemsAsync(
            context,
            existingTables,
            requiredSchema,
            cancellationToken);
        if (schemaProblems.Count > 0)
        {
            throw new InvalidOperationException(
                "The existing SQLite database cannot be adopted by EF " +
                "Core migrations because its legacy schema is incomplete: " +
                string.Join("; ", schemaProblems) + ". Restore a backup " +
                "and run the previous SafeSpend release once before " +
                "upgrading.");
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT
                    "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            """,
            cancellationToken);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO "__EFMigrationsHistory"
                ("MigrationId", "ProductVersion")
            VALUES ({initialMigrationId}, {ProductInfo.GetVersion()});
            """,
            cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<HashSet<string>> GetExistingTablesAsync(
        DbContext context,
        CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "name"
                FROM "sqlite_master"
                WHERE "type" = 'table'
                  AND "name" NOT LIKE 'sqlite_%'
                  AND "name" <> '__EFMigrationsHistory';
                """;

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        return tables;
    }

    private static async Task<List<string>> FindSchemaProblemsAsync(
        DbContext context,
        IReadOnlySet<string> existingTables,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>
            requiredSchema,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        foreach (var (tableName, requiredColumns) in requiredSchema)
        {
            if (!existingTables.Contains(tableName))
            {
                problems.Add($"missing table {tableName}");
                continue;
            }

            var existingColumns = await GetExistingColumnsAsync(
                context,
                tableName,
                cancellationToken);
            var missingColumns = requiredColumns
                .Where(column => !existingColumns.Contains(column))
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (missingColumns.Length > 0)
            {
                problems.Add(
                    $"table {tableName} is missing " +
                    string.Join(", ", missingColumns));
            }
        }

        return problems;
    }

    private static async Task<HashSet<string>> GetExistingColumnsAsync(
        DbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            var escapedTableName = tableName.Replace("\"", "\"\"");
            command.CommandText =
                $"PRAGMA table_info(\"{escapedTableName}\");";

            await using var reader = await command.ExecuteReaderAsync(
                cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        return columns;
    }
}
