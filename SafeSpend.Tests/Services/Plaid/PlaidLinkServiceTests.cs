using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Transactions;
using SafeSpend.Web.Services.Identity;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Services.Plaid;

public sealed class PlaidLinkServiceTests
{
    [Fact]
    public async Task GetCheckingAndSavingsAccountsAsync_FiltersAndMapsAccounts()
    {
        var plaidApi = new FakePlaidApi
        {
            AccountsResponse = new AccountsGetResponse
            {
                Accounts =
                [
                    new Account
                    {
                        AccountId = "checking-id",
                        Name = "Everyday Checking",
                        OfficialName = "Everyday Checking Account",
                        Mask = "1234",
                        Type = AccountType.Depository,
                        Subtype = AccountSubtype.Checking,
                        Balances = new AccountBalance
                        {
                            Available = 1250.25m,
                            Current = 1400.25m,
                            IsoCurrencyCode = "USD"
                        }
                    },
                    new Account
                    {
                        AccountId = "savings-id",
                        Name = "Rainy Day Savings",
                        Mask = "5678",
                        Type = AccountType.Depository,
                        Subtype = AccountSubtype.Savings,
                        Balances = new AccountBalance
                        {
                            Available = 5000m,
                            Current = 5000m,
                            IsoCurrencyCode = "USD"
                        }
                    },
                    new Account
                    {
                        AccountId = "credit-id",
                        Name = "Credit Card",
                        Type = AccountType.Credit,
                        Subtype = AccountSubtype.CreditCard,
                        Balances = new AccountBalance
                        {
                            Current = 250m,
                            IsoCurrencyCode = "USD"
                        }
                    }
                ]
            }
        };
        var transactionStore = new FakePlaidTransactionStore();
        var connectionStore = CreateConnectedStore();
        var service = CreateService(
            plaidApi,
            transactionStore,
            connectionStore);

        var accounts = await service.GetCheckingAndSavingsAccountsAsync();

        Assert.Equal(2, accounts.Count);
        Assert.Equal("Everyday Checking", accounts[0].Name);
        Assert.Equal("Checking", accounts[0].Subtype);
        Assert.Equal(1250.25m, accounts[0].AvailableBalance);
        Assert.Equal(1400.25m, accounts[0].CurrentBalance);
        Assert.Equal("Rainy Day Savings", accounts[1].Name);
        Assert.Equal("Savings", accounts[1].Subtype);
        Assert.Equal("USD", accounts[1].CurrencyCode);
    }

    [Fact]
    public async Task SyncTransactionsAsync_ConsumesPagesAndPersistsFinalCursor()
    {
        var plaidApi = new FakePlaidApi();
        plaidApi.TransactionResponses.Enqueue(
            new TransactionsSyncResponse
            {
                Added =
                [
                    new Transaction
                    {
                        TransactionId = "added-id",
                        AccountId = "checking-id",
                        Date = new DateOnly(2026, 9, 20),
                        Amount = 42.50m,
                        MerchantName = "Coffee Shop",
                        IsoCurrencyCode = "USD",
                        Pending = false,
                        PersonalFinanceCategory =
                            new PersonalFinanceCategory
                            {
                                Primary = "FOOD_AND_DRINK",
                                Detailed = "COFFEE"
                            }
                    }
                ],
                NextCursor = "page-one",
                HasMore = true
            });
        plaidApi.TransactionResponses.Enqueue(
            new TransactionsSyncResponse
            {
                Modified =
                [
                    new Transaction
                    {
                        TransactionId = "modified-id",
                        AccountId = "savings-id",
                        Date = new DateOnly(2026, 9, 19),
                        Amount = -100m,
                        OriginalDescription = "PAYROLL",
                        IsoCurrencyCode = "USD",
                        Pending = true
                    }
                ],
                Removed =
                [
                    new RemovedTransaction
                    {
                        TransactionId = "removed-id",
                        AccountId = "checking-id"
                    }
                ],
                NextCursor = "final-cursor",
                HasMore = false
            });

        var transactionStore = new FakePlaidTransactionStore();
        var connectionStore = CreateConnectedStore();
        var service = CreateService(
            plaidApi,
            transactionStore,
            connectionStore);

        var result = await service.SyncTransactionsAsync();

        Assert.Single(result.Added);
        Assert.Equal("Coffee Shop", result.Added[0].Name);
        Assert.Equal("FOOD_AND_DRINK", result.Added[0].PersonalFinancePrimaryCategory);
        Assert.Single(result.Modified);
        Assert.Equal("PAYROLL", result.Modified[0].Name);
        Assert.True(result.Modified[0].IsPending);
        Assert.Single(result.Removed);
        Assert.Equal("removed-id", result.Removed[0].TransactionId);
        Assert.Equal("final-cursor", result.NextCursor);
        Assert.Equal([null, "page-one"], plaidApi.TransactionCursors);
        Assert.Equal("final-cursor", connectionStore.Connection!.TransactionCursor);
        Assert.Single(transactionStore.AppliedResults);
        Assert.Equal("final-cursor", transactionStore.Cursor);

        plaidApi.TransactionResponses.Enqueue(
            new TransactionsSyncResponse
            {
                NextCursor = "next-cursor",
                HasMore = false
            });

        await service.SyncTransactionsAsync();

        Assert.Equal("final-cursor", plaidApi.TransactionCursors[2]);
    }

    [Fact]
    public async Task ExchangePublicTokenAsync_PersistsConnectionAndCursor()
    {
        var plaidApi = new FakePlaidApi();
        var transactionStore = new FakePlaidTransactionStore
        {
            Cursor = "persisted-cursor"
        };
        var connectionStore = new FakePlaidConnectionStore();
        var service = CreateService(
            plaidApi,
            transactionStore,
            connectionStore);

        await service.ExchangePublicTokenAsync("public-token");

        Assert.NotNull(connectionStore.Connection);
        Assert.Equal("item-id", connectionStore.Connection.ItemId);
        Assert.Equal(
            "persisted-cursor",
            connectionStore.Connection.TransactionCursor);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesRemoteItemAndLocalData()
    {
        var plaidApi = new FakePlaidApi();
        var transactionStore = new FakePlaidTransactionStore();
        var connectionStore = CreateConnectedStore();
        var service = CreateService(
            plaidApi,
            transactionStore,
            connectionStore);

        await service.DisconnectAsync();

        Assert.Equal("test-connection-token", plaidApi.RemovedAccessToken);
        Assert.Null(connectionStore.Connection);
        Assert.True(transactionStore.WasDeleted);
    }

    [Fact]
    public async Task ForgetUnavailableConnectionAsync_RemovesOnlyLocalData()
    {
        var plaidApi = new FakePlaidApi();
        var transactionStore = new FakePlaidTransactionStore();
        var connectionStore = CreateConnectedStore();
        var service = CreateService(
            plaidApi,
            transactionStore,
            connectionStore);

        await service.ForgetUnavailableConnectionAsync();

        Assert.Null(plaidApi.RemovedAccessToken);
        Assert.Null(connectionStore.Connection);
        Assert.True(transactionStore.WasDeleted);
    }

    private static PlaidLinkService CreateService(
        IPlaidApi plaidApi,
        FakePlaidTransactionStore transactionStore,
        FakePlaidConnectionStore connectionStore)
    {
        return new PlaidLinkService(
            plaidApi,
            connectionStore,
            transactionStore,
            new FakeCurrentUserContext(),
            new ImmediatePlaidSyncCoordinator(),
            new PlaidSyncQueue());
    }

    private static FakePlaidConnectionStore CreateConnectedStore()
    {
        return new FakePlaidConnectionStore
        {
            Connection = new PlaidConnection(
                "user-id",
                "item-id",
                "test-connection-token",
                null)
        };
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public string GetRequiredUserId() => "user-id";
    }

    private sealed class FakePlaidApi : IPlaidApi
    {
        public AccountsGetResponse AccountsResponse { get; init; } =
            new();

        public Queue<TransactionsSyncResponse> TransactionResponses { get; } =
            new();

        public List<string?> TransactionCursors { get; } = [];

        public string? RemovedAccessToken { get; private set; }

        public Task<string> CreateLinkTokenAsync(
            string clientUserId,
            string? accessToken) =>
            Task.FromResult("link-token");

        public Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
            string publicToken) =>
            Task.FromResult(
                new PlaidTokenExchangeResult(
                    "test-connection-token",
                    "item-id"));

        public Task RemoveItemAsync(string accessToken) =>
            RemoveItemAsyncCore(accessToken);

        private Task RemoveItemAsyncCore(string accessToken)
        {
            RemovedAccessToken = accessToken;
            return Task.CompletedTask;
        }

        public Task<AccountsGetResponse> GetAccountsAsync(
            string accessToken) =>
            Task.FromResult(AccountsResponse);

        public Task<TransactionsSyncResponse> SyncTransactionsAsync(
            string accessToken,
            string? cursor,
            int count)
        {
            TransactionCursors.Add(cursor);
            return Task.FromResult(TransactionResponses.Dequeue());
        }

        public Task<PlaidWebhookVerificationKey>
            GetWebhookVerificationKeyAsync(string keyId) =>
            throw new NotSupportedException();
    }

    private sealed class FakePlaidConnectionStore : IPlaidConnectionStore
    {
        public PlaidConnection? Connection { get; set; }

        public Task<PlaidConnection?> GetAsync(string userId) =>
            Task.FromResult(Connection);

        public Task<PlaidConnection?> GetByItemIdAsync(string itemId) =>
            Task.FromResult(
                Connection?.ItemId == itemId ? Connection : null);

        public Task<IReadOnlyList<PlaidConnection>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PlaidConnection>>(
                Connection is null ? [] : [Connection]);

        public Task<string?> GetItemIdAsync(string userId) =>
            Task.FromResult(Connection?.ItemId);

        public Task SaveAsync(
            string userId,
            string itemId,
            string accessToken,
            string? transactionCursor)
        {
            Connection = new PlaidConnection(
                userId,
                itemId,
                accessToken,
                transactionCursor);
            return Task.CompletedTask;
        }

        public Task SetCursorAsync(string userId, string? transactionCursor)
        {
            Connection = Connection! with
            {
                TransactionCursor = transactionCursor
            };
            return Task.CompletedTask;
        }

        public Task SetWebhookStatusAsync(
            string userId,
            string status,
            string webhookCode,
            DateTimeOffset receivedAt)
        {
            Connection = Connection! with
            {
                Status = status,
                LastWebhookCode = webhookCode,
                LastWebhookAt = receivedAt
            };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string userId)
        {
            Connection = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaidTransactionStore : IPlaidTransactionStore
    {
        public string? Cursor { get; set; }

        public List<PlaidTransactionSyncResult> AppliedResults { get; } = [];

        public bool WasDeleted { get; private set; }

        public Task<string?> GetCursorAsync(string itemId) =>
            Task.FromResult(Cursor);

        public Task ApplySyncAsync(
            string itemId,
            PlaidTransactionSyncResult result)
        {
            Cursor = result.NextCursor;
            AppliedResults.Add(result);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PlaidTransactionSummary>>
            GetTransactionsAsync(string itemId) =>
            Task.FromResult<IReadOnlyList<PlaidTransactionSummary>>([]);

        public Task DeleteAsync(string itemId)
        {
            Cursor = null;
            WasDeleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediatePlaidSyncCoordinator
        : IPlaidSyncCoordinator
    {
        public Task<T> RunAsync<T>(
            string userId,
            Func<Task<T>> operation) => operation();
    }
}
