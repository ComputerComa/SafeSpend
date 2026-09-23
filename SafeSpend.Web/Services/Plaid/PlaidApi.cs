using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;
using Going.Plaid.WebhookVerificationKey;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidApi(
    PlaidClient plaidClient,
    IConfiguration configuration) : IPlaidApi
{
    public async Task<string> CreateLinkTokenAsync(
        string clientUserId,
        string? accessToken)
    {
        var response = await plaidClient.LinkTokenCreateAsync(
            new LinkTokenCreateRequest
            {
                ClientName = "SafeSpend",
                Language = Language.English,
                CountryCodes = [CountryCode.Us],
                Products = string.IsNullOrWhiteSpace(accessToken)
                    ? [Products.Transactions]
                    : null,
                AccessToken = accessToken,
                Webhook = configuration["SafeSpend:PlaidWebhookUrl"],
                User = new()
                {
                    ClientUserId = clientUserId
                }
            });

        if (string.IsNullOrWhiteSpace(response.LinkToken))
        {
            throw new InvalidOperationException(
                "Plaid did not return a Link token.");
        }

        return response.LinkToken;
    }

    public async Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
        string publicToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicToken);

        var response = await plaidClient.ItemPublicTokenExchangeAsync(
            new ItemPublicTokenExchangeRequest
            {
                PublicToken = publicToken
            });

        if (string.IsNullOrWhiteSpace(response.AccessToken) ||
            string.IsNullOrWhiteSpace(response.ItemId))
        {
            throw new InvalidOperationException(
                "Plaid did not return an access token and Item ID.");
        }

        return new PlaidTokenExchangeResult(
            response.AccessToken,
            response.ItemId);
    }

    public async Task RemoveItemAsync(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        await plaidClient.ItemRemoveAsync(
            new ItemRemoveRequest
            {
                AccessToken = accessToken
            });
    }

    public Task<AccountsGetResponse> GetAccountsAsync(
        string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        return plaidClient.AccountsGetAsync(
            new AccountsGetRequest
            {
                AccessToken = accessToken
            });
    }

    public Task<TransactionsSyncResponse> SyncTransactionsAsync(
        string accessToken,
        string? cursor,
        int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        return plaidClient.TransactionsSyncAsync(
            new TransactionsSyncRequest
            {
                AccessToken = accessToken,
                Cursor = cursor,
                Count = count,
                Options = new TransactionsSyncRequestOptions
                {
                    IncludeOriginalDescription = true
                }
            });
    }

    public async Task<PlaidWebhookVerificationKey>
        GetWebhookVerificationKeyAsync(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var response = await plaidClient.WebhookVerificationKeyGetAsync(
            new WebhookVerificationKeyGetRequest
            {
                KeyId = keyId
            });
        var key = response.Key;

        if (key is null ||
            string.IsNullOrWhiteSpace(key.Kid) ||
            string.IsNullOrWhiteSpace(key.Alg) ||
            string.IsNullOrWhiteSpace(key.Crv) ||
            string.IsNullOrWhiteSpace(key.X) ||
            string.IsNullOrWhiteSpace(key.Y))
        {
            throw new InvalidOperationException(
                "Plaid did not return a usable webhook verification key.");
        }

        return new PlaidWebhookVerificationKey(
            key.Kid,
            key.Alg,
            key.Crv,
            key.X,
            key.Y,
            key.ExpiredAt is null
                ? null
                : DateTimeOffset.FromUnixTimeSeconds(key.ExpiredAt.Value));
    }
}
