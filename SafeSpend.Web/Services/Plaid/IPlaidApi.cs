using Going.Plaid.Accounts;
using Going.Plaid.Transactions;

namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidApi
{
    Task<string> CreateLinkTokenAsync(
        string clientUserId,
        string? accessToken);

    Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
        string publicToken);

    Task RemoveItemAsync(string accessToken);

    Task<AccountsGetResponse> GetAccountsAsync(string accessToken);

    Task<TransactionsSyncResponse> SyncTransactionsAsync(
        string accessToken,
        string? cursor,
        int count);

    Task<PlaidWebhookVerificationKey> GetWebhookVerificationKeyAsync(
        string keyId);
}

public sealed record PlaidTokenExchangeResult(
    string AccessToken,
    string ItemId);

public sealed record PlaidWebhookVerificationKey(
    string KeyId,
    string Algorithm,
    string Curve,
    string X,
    string Y,
    DateTimeOffset? ExpiresAt);
