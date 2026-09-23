using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Services.Plaid;

public sealed class PlaidConnectionStoreTests
{
    [Fact]
    public async Task PersistsProtectedConnectionAndSupportsRemoval()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-connection-{Guid.NewGuid():N}.db");
        var keyPath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-keys-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(keyPath);
            var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var context = new SafeSpendDbContext(options))
            {
                await SafeSpendDatabaseInitializer.InitializeAsync(context);
            }

            var dataProtectionProvider =
                DataProtectionProvider.Create(keyPath);
            var store = new PlaidConnectionStore(
                new TestDbContextFactory(options),
                dataProtectionProvider,
                new LegacyPlaidAccessTokenProtector(keyPath, []));

            await store.SaveAsync(
                "user-id",
                "item-id",
                "production-access-token",
                "cursor-one");

            await using (var context = new SafeSpendDbContext(options))
            {
                var entity = await context.PlaidConnections
                    .AsNoTracking()
                    .SingleAsync();

                Assert.NotEqual(
                    "production-access-token",
                    entity.ProtectedAccessToken);
            }

            var connection = await store.GetAsync("user-id");
            Assert.NotNull(connection);
            Assert.Equal("item-id", connection.ItemId);
            Assert.Equal(
                "production-access-token",
                connection.AccessToken);
            Assert.Equal("cursor-one", connection.TransactionCursor);

            await store.SetCursorAsync("user-id", "cursor-two");
            Assert.Equal(
                "cursor-two",
                (await store.GetAsync("user-id"))!.TransactionCursor);

            await store.DeleteAsync("user-id");
            Assert.Null(await store.GetAsync("user-id"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            if (Directory.Exists(keyPath))
            {
                Directory.Delete(keyPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReprotectsTokenCreatedWithLegacyApplicationName()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-legacy-connection-{Guid.NewGuid():N}.db");
        var keyPath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-legacy-keys-{Guid.NewGuid():N}");
        const string legacyApplicationName =
            "/opt/safespend/releases/v1.0.0/";

        try
        {
            Directory.CreateDirectory(keyPath);
            var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var context = new SafeSpendDbContext(options))
            {
                await SafeSpendDatabaseInitializer.InitializeAsync(context);

                var legacyProvider = DataProtectionProvider.Create(
                    new DirectoryInfo(keyPath),
                    builder => builder.SetApplicationName(
                        legacyApplicationName));
                var legacyProtector = legacyProvider.CreateProtector(
                    PlaidConnectionStore.AccessTokenPurpose);
                context.PlaidConnections.Add(new PlaidConnectionEntity
                {
                    UserId = "user-id",
                    ItemId = "item-id",
                    ProtectedAccessToken = legacyProtector.Protect(
                        "legacy-access-token"),
                    Status = "Connected"
                });
                await context.SaveChangesAsync();
            }

            var stableProvider = DataProtectionProvider.Create(
                new DirectoryInfo(keyPath),
                builder => builder.SetApplicationName(
                    PlaidConnectionStore.ApplicationName));
            using var legacyFallback =
                new LegacyPlaidAccessTokenProtector(
                    keyPath,
                    [legacyApplicationName]);
            var store = new PlaidConnectionStore(
                new TestDbContextFactory(options),
                stableProvider,
                legacyFallback);

            var connection = await store.GetAsync("user-id");

            Assert.NotNull(connection);
            Assert.Equal("legacy-access-token", connection.AccessToken);

            await using var verificationContext =
                new SafeSpendDbContext(options);
            var reprotectedValue = await verificationContext
                .PlaidConnections
                .AsNoTracking()
                .Select(row => row.ProtectedAccessToken)
                .SingleAsync();
            var stableProtector = stableProvider.CreateProtector(
                PlaidConnectionStore.AccessTokenPurpose);
            Assert.Equal(
                "legacy-access-token",
                stableProtector.Unprotect(reprotectedValue));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            if (Directory.Exists(keyPath))
            {
                Directory.Delete(keyPath, recursive: true);
            }
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<SafeSpendDbContext> options)
        : IDbContextFactory<SafeSpendDbContext>
    {
        public SafeSpendDbContext CreateDbContext() =>
            new(options);

        public Task<SafeSpendDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafeSpendDbContext(options));
    }
}
