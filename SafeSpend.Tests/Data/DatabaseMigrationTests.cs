using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Identity;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Data;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task SafeSpendMigrationAdoptsLegacyDatabaseAndPreservesData()
    {
        var databasePath = CreateDatabasePath("legacy-app");
        var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using (var legacyContext =
                         new SafeSpendDbContext(options))
            {
                await legacyContext.Database.EnsureCreatedAsync();
                legacyContext.BillSchedules.Add(new BillScheduleEntity
                {
                    Name = "Mortgage",
                    NextDueDate = new DateOnly(2026, 10, 1),
                    AmountCents = 150_000,
                    Frequency = 3
                });
                await legacyContext.SaveChangesAsync();
            }

            await using (var migrationContext =
                         new SafeSpendDbContext(options))
            {
                await SafeSpendDatabaseInitializer.InitializeAsync(
                    migrationContext);
            }

            await using var verificationContext =
                new SafeSpendDbContext(options);
            var bill = await verificationContext.BillSchedules
                .AsNoTracking()
                .SingleAsync();
            var appliedMigrations = await verificationContext.Database
                .GetAppliedMigrationsAsync();

            Assert.Equal("Mortgage", bill.Name);
            Assert.Contains(
                SafeSpendDatabaseInitializer.InitialMigrationId,
                appliedMigrations);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task IdentityMigrationAdoptsLegacyDatabaseAndPreservesUser()
    {
        var databasePath = CreateDatabasePath("legacy-identity");
        var options =
            new DbContextOptionsBuilder<SafeSpendIdentityDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

        try
        {
            await using (var legacyContext =
                         new SafeSpendIdentityDbContext(options))
            {
                await legacyContext.Database.EnsureCreatedAsync();
                legacyContext.Users.Add(new ApplicationUser
                {
                    Id = "legacy-user",
                    UserName = "admin@example.test",
                    NormalizedUserName = "ADMIN@EXAMPLE.TEST",
                    Email = "admin@example.test",
                    NormalizedEmail = "ADMIN@EXAMPLE.TEST",
                    SecurityStamp = "legacy-security-stamp"
                });
                await legacyContext.SaveChangesAsync();
            }

            await using (var migrationContext =
                         new SafeSpendIdentityDbContext(options))
            {
                await IdentityDatabaseInitializer.InitializeAsync(
                    migrationContext);
            }

            await using var verificationContext =
                new SafeSpendIdentityDbContext(options);
            var user = await verificationContext.Users
                .AsNoTracking()
                .SingleAsync();
            var appliedMigrations = await verificationContext.Database
                .GetAppliedMigrationsAsync();

            Assert.Equal("admin@example.test", user.Email);
            Assert.Contains(
                IdentityDatabaseInitializer.InitialMigrationId,
                appliedMigrations);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task MigrationRejectsIncompleteLegacySchema()
    {
        var databasePath = CreateDatabasePath("incomplete-app");
        var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using var context = new SafeSpendDbContext(options);
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE \"PlaidItems\" " +
                "(\"ItemId\" TEXT NOT NULL PRIMARY KEY);");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SafeSpendDatabaseInitializer.InitializeAsync(context));

            Assert.Contains("legacy schema is incomplete", exception.Message);
            Assert.Contains("PlaidItems is missing TransactionCursor", exception.Message);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static string CreateDatabasePath(string name) =>
        Path.Combine(
            Path.GetTempPath(),
            $"safespend-{name}-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }
}
