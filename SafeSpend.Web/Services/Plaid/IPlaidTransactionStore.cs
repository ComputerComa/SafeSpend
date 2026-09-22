namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidTransactionStore
{
    Task<string?> GetCursorAsync(string itemId);

    Task ApplySyncAsync(
        string itemId,
        PlaidTransactionSyncResult result);

    Task<IReadOnlyList<PlaidTransactionSummary>> GetTransactionsAsync(
        string itemId);
}
