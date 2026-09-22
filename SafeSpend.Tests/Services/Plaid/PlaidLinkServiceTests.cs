using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Transactions;
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
        var state = CreateConnectedState();
        var transactionStore = new FakePlaidTransactionStore();
        var service = new PlaidLinkService(
            plaidApi,
            state,
            transactionStore);

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

        var state = CreateConnectedState();
        var transactionStore = new FakePlaidTransactionStore();
        var service = new PlaidLinkService(
            plaidApi,
            state,
            transactionStore);

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
        Assert.Equal("final-cursor", state.TransactionCursor);
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
    public async Task ExchangePublicTokenAsync_RestoresPersistedCursor()
    {
        var plaidApi = new FakePlaidApi();
        var transactionStore = new FakePlaidTransactionStore
        {
            Cursor = "persisted-cursor"
        };
        var state = new PlaidConnectionState();
        var service = new PlaidLinkService(
            plaidApi,
            state,
            transactionStore);

        await service.ExchangePublicTokenAsync("public-token");

        Assert.Equal("persisted-cursor", state.TransactionCursor);
    }

    private static PlaidConnectionState CreateConnectedState()
    {
        var state = new PlaidConnectionState();
        state.SetConnection("test-connection-token", "item-id");
        return state;
    }

    private sealed class FakePlaidApi : IPlaidApi
    {
        public AccountsGetResponse AccountsResponse { get; init; } =
            new();

        public Queue<TransactionsSyncResponse> TransactionResponses { get; } =
            new();

        public List<string?> TransactionCursors { get; } = [];

        public Task<string> CreateLinkTokenAsync() =>
            Task.FromResult("link-token");

        public Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
            string publicToken) =>
            Task.FromResult(
                new PlaidTokenExchangeResult(
                    "test-connection-token",
                    "item-id"));

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
    }

    private sealed class FakePlaidTransactionStore : IPlaidTransactionStore
    {
        public string? Cursor { get; set; }

        public List<PlaidTransactionSyncResult> AppliedResults { get; } = [];

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
    }
}
