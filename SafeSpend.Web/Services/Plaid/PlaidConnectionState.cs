namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidConnectionState
{
    private readonly object _syncRoot = new();

    public string? ItemId { get; private set; }

    public string? TransactionCursor { get; private set; }

    public bool IsConnected
    {
        get
        {
            lock (_syncRoot)
            {
                return !string.IsNullOrWhiteSpace(_accessToken);
            }
        }
    }

    private string? _accessToken;

    internal bool TryGetAccessToken(out string accessToken)
    {
        lock (_syncRoot)
        {
            if (string.IsNullOrWhiteSpace(_accessToken))
            {
                accessToken = string.Empty;
                return false;
            }

            accessToken = _accessToken;
            return true;
        }
    }

    public void SetConnection(
        string accessToken,
        string itemId,
        string? transactionCursor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        lock (_syncRoot)
        {
            _accessToken = accessToken;
            ItemId = itemId;
            TransactionCursor = transactionCursor;
        }
    }

    public void SetTransactionCursor(string? cursor)
    {
        lock (_syncRoot)
        {
            TransactionCursor = cursor;
        }
    }
}
