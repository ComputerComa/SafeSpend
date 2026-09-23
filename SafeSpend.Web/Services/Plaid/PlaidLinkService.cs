using Going.Plaid.Entity;
using SafeSpend.Web.Services.Identity;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidLinkService(
    IPlaidApi plaidApi,
    IPlaidConnectionStore connectionStore,
    IPlaidTransactionStore transactionStore,
    ICurrentUserContext currentUser,
    IPlaidSyncCoordinator syncCoordinator,
    IPlaidSyncQueue syncQueue)
{
    public async Task<string> CreateLinkTokenAsync()
    {
        var userId = currentUser.GetRequiredUserId();
        var connection = await connectionStore.GetAsync(userId);
        return await plaidApi.CreateLinkTokenAsync(
            userId,
            connection?.AccessToken);
    }

    public async Task<bool> IsConnectedAsync()
    {
        var connection = await connectionStore.GetAsync(
            currentUser.GetRequiredUserId());
        return connection is not null;
    }

    public async Task<string> ExchangePublicTokenAsync(string publicToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicToken);

        var userId = currentUser.GetRequiredUserId();
        var response = await plaidApi.ExchangePublicTokenAsync(publicToken);

        if (string.IsNullOrWhiteSpace(response.AccessToken) ||
            string.IsNullOrWhiteSpace(response.ItemId))
        {
            throw new InvalidOperationException(
                "Plaid did not return an access token and Item ID.");
        }

        var transactionCursor = await transactionStore
            .GetCursorAsync(response.ItemId);

        await connectionStore.SaveAsync(
            userId,
            response.ItemId,
            response.AccessToken,
            transactionCursor);
        syncQueue.Enqueue(userId);

        return response.ItemId;
    }

    public async Task<IReadOnlyList<PlaidAccountSummary>>
        GetCheckingAndSavingsAccountsAsync()
    {
        var connection = await GetRequiredConnectionAsync();
        var response = await plaidApi.GetAccountsAsync(
            connection.AccessToken);

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
        return await SyncTransactionsForUserAsync(
            currentUser.GetRequiredUserId());
    }

    public async Task<PlaidTransactionSyncResult> SyncTransactionsForUserAsync(
        string userId)
    {
        return await syncCoordinator.RunAsync(
            userId,
            () => SyncTransactionsForUserCoreAsync(userId));
    }

    public async Task<string> GetConnectionStatusAsync()
    {
        var connection = await connectionStore.GetAsync(
            currentUser.GetRequiredUserId());
        return connection?.Status ?? "Disconnected";
    }

    private async Task<PlaidTransactionSyncResult>
        SyncTransactionsForUserCoreAsync(string userId)
    {
        var connection = await connectionStore.GetAsync(
            userId);

        return await SyncTransactionsForConnectionAsync(
            connection ?? throw new InvalidOperationException(
                "A Plaid connection is required."));
    }

    private async Task<PlaidTransactionSyncResult>
        SyncTransactionsForConnectionAsync(PlaidConnection connection)
    {
        var cursor = connection.TransactionCursor ?? await transactionStore
            .GetCursorAsync(connection.ItemId);
        var added = new List<PlaidTransactionSummary>();
        var modified = new List<PlaidTransactionSummary>();
        var removed = new List<PlaidRemovedTransactionSummary>();
        var hasMore = false;

        do
        {
            var response = await plaidApi.SyncTransactionsAsync(
                connection.AccessToken,
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

        await transactionStore.ApplySyncAsync(connection.ItemId, result);
        await connectionStore.SetCursorAsync(
            connection.UserId,
            cursor);

        return result;
    }

    public async Task<IReadOnlyList<PlaidTransactionSummary>>
        GetStoredTransactionsAsync()
    {
        var connection = await GetRequiredConnectionAsync();
        return await transactionStore.GetTransactionsAsync(
            connection.ItemId);
    }

    public async Task DisconnectAsync()
    {
        var userId = currentUser.GetRequiredUserId();
        await syncCoordinator.RunAsync(
            userId,
            async () =>
            {
                var connection = await connectionStore.GetAsync(userId);

                if (connection is null)
                {
                    return true;
                }

                await plaidApi.RemoveItemAsync(connection.AccessToken);
                await transactionStore.DeleteAsync(connection.ItemId);
                await connectionStore.DeleteAsync(userId);
                return true;
            });
    }

    public async Task ForgetUnavailableConnectionAsync()
    {
        var userId = currentUser.GetRequiredUserId();
        await syncCoordinator.RunAsync(
            userId,
            async () =>
            {
                var itemId = await connectionStore.GetItemIdAsync(userId);
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    await transactionStore.DeleteAsync(itemId);
                }

                await connectionStore.DeleteAsync(userId);
                return true;
            });
    }

    private async Task<PlaidConnection> GetRequiredConnectionAsync()
    {
        var connection = await connectionStore.GetAsync(
            currentUser.GetRequiredUserId());

        return connection ?? throw new InvalidOperationException(
            "A Plaid connection is required.");
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
