using Going.Plaid.Entity;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidLinkService(
    IPlaidApi plaidApi,
    PlaidConnectionState connectionState,
    IPlaidTransactionStore transactionStore)
{
    public async Task<string> CreateLinkTokenAsync()
    {
        return await plaidApi.CreateLinkTokenAsync();
    }

    public async Task<string> ExchangePublicTokenAsync(string publicToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicToken);

        var response = await plaidApi.ExchangePublicTokenAsync(publicToken);

        if (string.IsNullOrWhiteSpace(response.AccessToken) ||
            string.IsNullOrWhiteSpace(response.ItemId))
        {
            throw new InvalidOperationException(
                "Plaid did not return an access token and Item ID.");
        }

        // Never send the access token to the browser.
        var transactionCursor = await transactionStore
            .GetCursorAsync(response.ItemId);

        connectionState.SetConnection(
            response.AccessToken,
            response.ItemId,
            transactionCursor);

        return response.ItemId;
    }

    public async Task<IReadOnlyList<PlaidAccountSummary>>
        GetCheckingAndSavingsAccountsAsync()
    {
        var accessToken = GetRequiredAccessToken();
        var response = await plaidApi.GetAccountsAsync(accessToken);

        return (response.Accounts ?? [])
            .Where(account =>
                account.Type == AccountType.Depository &&
                (account.Subtype == AccountSubtype.Checking ||
                 account.Subtype == AccountSubtype.Savings))
            .Select(MapAccount)
            .ToArray();
    }

    public async Task<PlaidTransactionSyncResult> SyncTransactionsAsync()
    {
        var accessToken = GetRequiredAccessToken();
        var itemId = GetRequiredItemId();
        var cursor = connectionState.TransactionCursor;
        var added = new List<PlaidTransactionSummary>();
        var modified = new List<PlaidTransactionSummary>();
        var removed = new List<PlaidRemovedTransactionSummary>();
        var hasMore = false;

        do
        {
            var response = await plaidApi.SyncTransactionsAsync(
                accessToken,
                cursor,
                count: 500);

            added.AddRange((response.Added ?? [])
                .Select(MapTransaction));
            modified.AddRange((response.Modified ?? [])
                .Select(MapTransaction));
            removed.AddRange((response.Removed ?? [])
                .Select(transaction =>
                    new PlaidRemovedTransactionSummary(
                        transaction.TransactionId,
                        transaction.AccountId)));

            cursor = NormalizeCursor(response.NextCursor);
            hasMore = response.HasMore;
        }
        while (hasMore);

        var result = new PlaidTransactionSyncResult(
            added,
            modified,
            removed,
            cursor);

        await transactionStore.ApplySyncAsync(itemId, result);
        connectionState.SetTransactionCursor(cursor);

        return result;
    }

    public async Task<IReadOnlyList<PlaidTransactionSummary>>
        GetStoredTransactionsAsync()
    {
        return await transactionStore.GetTransactionsAsync(
            GetRequiredItemId());
    }

    private string GetRequiredAccessToken()
    {
        if (!connectionState.TryGetAccessToken(out var accessToken))
        {
            throw new InvalidOperationException(
                "A Plaid connection is required.");
        }

        return accessToken;
    }

    private string GetRequiredItemId()
    {
        if (string.IsNullOrWhiteSpace(connectionState.ItemId))
        {
            throw new InvalidOperationException(
                "A Plaid connection is required.");
        }

        return connectionState.ItemId;
    }

    private static string? NormalizeCursor(string? cursor)
    {
        return string.IsNullOrWhiteSpace(cursor) ? null : cursor;
    }

    private static PlaidAccountSummary MapAccount(Account account)
    {
        var balances = account.Balances;

        return new PlaidAccountSummary(
            account.AccountId,
            account.Name,
            account.OfficialName,
            account.Mask,
            account.Type.ToString(),
            account.Subtype?.ToString() ?? "unknown",
            balances?.Available,
            balances?.Current,
            balances?.IsoCurrencyCode ??
            balances?.UnofficialCurrencyCode ??
            "USD");
    }

    private static PlaidTransactionSummary MapTransaction(
        Transaction transaction)
    {
        var category = transaction.PersonalFinanceCategory;

        return new PlaidTransactionSummary(
            transaction.TransactionId ??
            throw new InvalidOperationException(
                "Plaid returned a transaction without an ID."),
            transaction.AccountId ??
            throw new InvalidOperationException(
                "Plaid returned a transaction without an account ID."),
            transaction.Date,
            transaction.Amount,
            transaction.MerchantName ??
            transaction.OriginalDescription ??
            "Unknown transaction",
            transaction.MerchantName,
            transaction.IsoCurrencyCode ??
            transaction.UnofficialCurrencyCode,
            transaction.Pending ?? false,
            category?.Primary,
            category?.Detailed);
    }
}
