using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Link;
using Going.Plaid.Transactions;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidApi(PlaidClient plaidClient) : IPlaidApi
{
    public async Task<string> CreateLinkTokenAsync()
    {
        var response = await plaidClient.LinkTokenCreateAsync(
            new LinkTokenCreateRequest
            {
                ClientName = "SafeSpend",
                Language = Language.English,
                CountryCodes = [CountryCode.Us],
                Products = [Products.Transactions],
                User = new()
                {
                    ClientUserId = "safespend-local-owner"
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
}
