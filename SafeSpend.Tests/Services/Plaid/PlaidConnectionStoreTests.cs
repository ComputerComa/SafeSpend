using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
                new LegacyPlaidAccessTokenProtector(keyPath, []),
                NullLogger<PlaidConnectionStore>.Instance);

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
        var stableKeyPath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-stable-keys-{Guid.NewGuid():N}");
        const string legacyApplicationName =
            "/opt/safespend/releases/v1.0.0/";

        try
        {
            Directory.CreateDirectory(keyPath);
            Directory.CreateDirectory(stableKeyPath);
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
                new DirectoryInfo(stableKeyPath),
                builder => builder.SetApplicationName(
                    PlaidConnectionStore.ApplicationName));
            using var legacyFallback =
                new LegacyPlaidAccessTokenProtector(
                    [stableKeyPath, keyPath],
                    [legacyApplicationName]);
            var store = new PlaidConnectionStore(
                new TestDbContextFactory(options),
                stableProvider,
                legacyFallback,
                NullLogger<PlaidConnectionStore>.Instance);

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
            if (Directory.Exists(stableKeyPath))
            {
                Directory.Delete(stableKeyPath, recursive: true);
            }
        }
    }

    [Fact]
    public void DiscoversKeyDirectoriesFromRetainedReleases()
    {
        var appRoot = Path.Combine(
            Path.GetTempPath(),
            $"safespend-releases-{Guid.NewGuid():N}");
        var primaryKeyPath = Path.Combine(appRoot, "persistent-data");
        var currentRelease = Path.Combine(appRoot, "releases", "v1.0.4");
        var legacyKeyPath = Path.Combine(
            appRoot,
            "releases",
            "v1.0.1",
            "App_Data",
            "Production");
        var archivedKeyPath = Path.Combine(
            primaryKeyPath,
            "legacy-data-protection-keys",
            "v1.0.0",
            "App_Data",
            "Production");

        try
        {
            Directory.CreateDirectory(primaryKeyPath);
            Directory.CreateDirectory(currentRelease);
            Directory.CreateDirectory(legacyKeyPath);
            Directory.CreateDirectory(archivedKeyPath);
            File.WriteAllText(
                Path.Combine(legacyKeyPath, "key-test.xml"),
                "test key location marker");
            File.WriteAllText(
                Path.Combine(archivedKeyPath, "key-archived.xml"),
                "test archived key location marker");

            var keyDirectories = LegacyPlaidAccessTokenProtector
                .LoadKeyDirectories(primaryKeyPath, currentRelease);

            Assert.Contains(Path.GetFullPath(primaryKeyPath), keyDirectories);
            Assert.Contains(Path.GetFullPath(legacyKeyPath), keyDirectories);
            Assert.Contains(Path.GetFullPath(archivedKeyPath), keyDirectories);
        }
        finally
        {
            if (Directory.Exists(appRoot))
            {
                Directory.Delete(appRoot, recursive: true);
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
