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
                await context.Database.EnsureCreatedAsync();
            }

            var dataProtectionProvider =
                DataProtectionProvider.Create(keyPath);
            var store = new PlaidConnectionStore(
                new TestDbContextFactory(options),
                dataProtectionProvider);

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
