namespace SafeSpend.Web.Services.Plaid;

public sealed record PlaidWebhookResult(
    bool IsValid,
    bool IsKnownConnection,
    bool SyncQueued);

public sealed class PlaidWebhookService(
    IPlaidWebhookVerifier verifier,
    IPlaidConnectionStore connectionStore,
    IPlaidSyncQueue syncQueue,
    ILogger<PlaidWebhookService> logger)
{
    public async Task<PlaidWebhookResult> HandleAsync(
        ReadOnlyMemory<byte> body,
        string? verificationHeader,
        CancellationToken cancellationToken)
    {
        var payload = await verifier.VerifyAsync(
            body,
            verificationHeader,
            cancellationToken);
        if (payload is null)
        {
            return new PlaidWebhookResult(false, false, false);
        }

        if (string.IsNullOrWhiteSpace(payload.ItemId))
        {
            return new PlaidWebhookResult(true, false, false);
        }

        var connection = await connectionStore.GetByItemIdAsync(
            payload.ItemId);
        if (connection is null)
        {
            return new PlaidWebhookResult(true, false, false);
        }

        await connectionStore.SetWebhookStatusAsync(
            connection.UserId,
            GetStatus(payload) ?? connection.Status,
            payload.WebhookCode,
            DateTimeOffset.UtcNow);

        var shouldSync = ShouldQueueSync(payload);
        if (shouldSync)
        {
            syncQueue.Enqueue(connection.UserId);
        }

        logger.LogInformation(
            "Plaid webhook accepted. Type: {WebhookType}; code: {WebhookCode}; sync queued: {SyncQueued}",
            payload.WebhookType,
            payload.WebhookCode,
            shouldSync);

        return new PlaidWebhookResult(true, true, shouldSync);
    }

    private static bool ShouldQueueSync(PlaidWebhookPayload payload)
    {
        if (!string.Equals(
                payload.WebhookType,
                "TRANSACTIONS",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return payload.WebhookCode is
            "SYNC_UPDATES_AVAILABLE" or
            "INITIAL_UPDATE" or
            "HISTORICAL_UPDATE" or
            "TRANSACTIONS_REMOVED" or
            "DEFAULT_UPDATE";
    }

    private static string? GetStatus(PlaidWebhookPayload payload)
    {
        if (string.Equals(
                payload.WebhookType,
                "ITEM",
                StringComparison.OrdinalIgnoreCase))
        {
            return payload.WebhookCode switch
            {
                "LOGIN_REPAIRED" => "Connected",
                "ITEM_LOGIN_REQUIRED" or
                "PENDING_EXPIRATION" or
                "PENDING_DISCONNECT" or
                "NEW_ACCOUNTS_AVAILABLE" or
                "USER_PERMISSION_REVOKED" or
                "USER_ACCOUNT_REVOKED" or
                "PRODUCT_PERMISSIONS_REQUIRED" => "ActionRequired",
                _ => null
            };
        }

        return null;
    }
}
