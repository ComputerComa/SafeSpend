namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidConnectionStore
{
    Task<PlaidConnection?> GetAsync(string userId);

    Task<PlaidConnection?> GetByItemIdAsync(string itemId);

    Task<IReadOnlyList<PlaidConnection>> GetAllAsync();

    Task<string?> GetItemIdAsync(string userId);

    Task SaveAsync(
        string userId,
        string itemId,
        string accessToken,
        string? transactionCursor);

    Task SetCursorAsync(string userId, string? transactionCursor);

    Task SetWebhookStatusAsync(
        string userId,
        string status,
        string webhookCode,
        DateTimeOffset receivedAt);

    Task DeleteAsync(string userId);
}

public sealed record PlaidConnection(
    string UserId,
    string ItemId,
    string AccessToken,
    string? TransactionCursor,
    string Status = "Connected",
    string? LastWebhookCode = null,
    DateTimeOffset? LastWebhookAt = null);
