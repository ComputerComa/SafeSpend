using Going.Plaid.Accounts;
using Going.Plaid.Transactions;

namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidApi
{
    Task<string> CreateLinkTokenAsync();

    Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
        string publicToken);

    Task<AccountsGetResponse> GetAccountsAsync(string accessToken);

    Task<TransactionsSyncResponse> SyncTransactionsAsync(
        string accessToken,
        string? cursor,
        int count);
}

public sealed record PlaidTokenExchangeResult(
    string AccessToken,
    string ItemId);
