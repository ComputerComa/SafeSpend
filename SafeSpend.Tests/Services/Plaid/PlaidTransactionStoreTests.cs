using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Services.Plaid;

public sealed class PlaidTransactionStoreTests
{
    [Fact]
    public async Task ApplySyncAsync_PersistsCursorAndTransactionChanges()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var context = new SafeSpendDbContext(options))
            {
                await SafeSpendDatabaseInitializer.InitializeAsync(context);
            }

            var factory = new TestDbContextFactory(options);
            var store = new PlaidTransactionStore(factory);

            await store.ApplySyncAsync(
                "item-id",
                new PlaidTransactionSyncResult(
                    Added:
                    [
                        CreateTransaction(
                            "transaction-one",
                            "Checking",
                            25m),
                        CreateTransaction(
                            "transaction-two",
                            "Savings",
                            75m)
                    ],
                    Modified: [],
                    Removed: [],
                    NextCursor: "cursor-one"));

            await store.ApplySyncAsync(
                "item-id",
                new PlaidTransactionSyncResult(
                    Added: [],
                    Modified:
                    [
                        CreateTransaction(
                            "transaction-one",
                            "Updated Checking",
                            30m)
                    ],
                    Removed:
                    [
                        new PlaidRemovedTransactionSummary(
                            "transaction-two",
                            "savings-id")
                    ],
                    NextCursor: "cursor-two"));

            var reloadedStore = new PlaidTransactionStore(
                new TestDbContextFactory(options));
            var transactions = await reloadedStore
                .GetTransactionsAsync("item-id");

            Assert.Equal("cursor-two", await reloadedStore
                .GetCursorAsync("item-id"));
            var transaction = Assert.Single(transactions);
            Assert.Equal("transaction-one", transaction.TransactionId);
            Assert.Equal("Updated Checking", transaction.Name);
            Assert.Equal(30m, transaction.Amount);

            await reloadedStore.DeleteAsync("item-id");
            Assert.Empty(await reloadedStore.GetTransactionsAsync("item-id"));
            Assert.Null(await reloadedStore.GetCursorAsync("item-id"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static PlaidTransactionSummary CreateTransaction(
        string transactionId,
        string name,
        decimal amount)
    {
        return new PlaidTransactionSummary(
            transactionId,
            "checking-id",
            new DateOnly(2026, 9, 22),
            amount,
            name,
            null,
            "USD",
            false,
            null,
            null);
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
