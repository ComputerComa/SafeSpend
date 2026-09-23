using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Going.Plaid.Accounts;
using Going.Plaid.Transactions;
using Microsoft.Extensions.Logging.Abstractions;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Services.Plaid;

public sealed class PlaidWebhookTests
{
    [Fact]
    public async Task Verifier_AcceptsPlaidStyleSignature()
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"webhook_type\":\"TRANSACTIONS\",\"webhook_code\":\"SYNC_UPDATES_AVAILABLE\",\"item_id\":\"item-id\"}");
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = signingKey.ExportParameters(false);
        var header = Base64Url("{\"alg\":\"ES256\",\"kid\":\"key-id\"}");
        var payload = Base64Url(JsonSerializer.Serialize(new
        {
            webhook_type = "TRANSACTIONS",
            webhook_code = "SYNC_UPDATES_AVAILABLE",
            item_id = "item-id",
            request_body_sha256 = Convert.ToHexString(
                SHA256.HashData(body)).ToLowerInvariant(),
            iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }));
        var signedData = Encoding.ASCII.GetBytes($"{header}.{payload}");
        var derSignature = signingKey.SignData(
            signedData,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var token = $"{header}.{payload}.{Base64Url(ToJoseSignature(derSignature))}";

        var verifier = new PlaidWebhookVerifier(
            new FakePlaidApi
            {
                VerificationKey = new PlaidWebhookVerificationKey(
                    "key-id",
                    "ES256",
                    "P-256",
                    Base64Url(parameters.Q.X!),
                    Base64Url(parameters.Q.Y!),
                    null)
            });

        var result = await verifier.VerifyAsync(body, token, default);

        Assert.NotNull(result);
        Assert.Equal("TRANSACTIONS", result.WebhookType);
        Assert.Equal("SYNC_UPDATES_AVAILABLE", result.WebhookCode);
        Assert.Equal("item-id", result.ItemId);
    }

    [Fact]
    public async Task WebhookService_RecordsItemRecoveryAndQueuesSync()
    {
        var store = new FakeConnectionStore
        {
            Connection = new PlaidConnection(
                "user-id",
                "item-id",
                "protected-in-storage-token",
                null)
        };
        var queue = new PlaidSyncQueue();
        var service = new PlaidWebhookService(
            new FakeVerifier(new PlaidWebhookPayload(
                "ITEM",
                "ITEM_LOGIN_REQUIRED",
                "item-id")),
            store,
            queue,
            NullLogger<PlaidWebhookService>.Instance);

        var result = await service.HandleAsync(
            Encoding.UTF8.GetBytes("{}"),
            "verified",
            default);

        Assert.True(result.IsValid);
        Assert.True(result.IsKnownConnection);
        Assert.False(result.SyncQueued);
        Assert.Equal("ActionRequired", store.Connection!.Status);
        Assert.Equal("ITEM_LOGIN_REQUIRED", store.Connection.LastWebhookCode);
    }

    [Fact]
    public async Task WebhookService_QueuesTransactionSync()
    {
        var store = new FakeConnectionStore
        {
            Connection = new PlaidConnection(
                "user-id",
                "item-id",
                "protected-in-storage-token",
                null)
        };
        var queue = new PlaidSyncQueue();
        var service = new PlaidWebhookService(
            new FakeVerifier(new PlaidWebhookPayload(
                "TRANSACTIONS",
                "SYNC_UPDATES_AVAILABLE",
                "item-id")),
            store,
            queue,
            NullLogger<PlaidWebhookService>.Instance);

        var result = await service.HandleAsync(
            Encoding.UTF8.GetBytes("{}"),
            "verified",
            default);

        Assert.True(result.IsValid);
        Assert.True(result.IsKnownConnection);
        Assert.True(result.SyncQueued);
        Assert.Equal("user-id", await queue.DequeueAsync(default));
    }

    private static string Base64Url(string value) =>
        Base64Url(Encoding.UTF8.GetBytes(value));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] ToJoseSignature(byte[] der)
    {
        var offset = 2;
        var rLength = der[offset + 1];
        var r = der[(offset + 2)..(offset + 2 + rLength)];
        offset += 2 + rLength;
        var sLength = der[offset + 1];
        var s = der[(offset + 2)..(offset + 2 + sLength)];
        var jose = new byte[64];
        r[^Math.Min(32, r.Length)..].CopyTo(
            jose.AsSpan(32 - Math.Min(32, r.Length)));
        s[^Math.Min(32, s.Length)..].CopyTo(
            jose.AsSpan(64 - Math.Min(32, s.Length)));
        return jose;
    }

    private sealed class FakeVerifier(PlaidWebhookPayload payload)
        : IPlaidWebhookVerifier
    {
        public Task<PlaidWebhookPayload?> VerifyAsync(
            ReadOnlyMemory<byte> body,
            string? verificationHeader,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlaidWebhookPayload?>(payload);
    }

    private sealed class FakeConnectionStore : IPlaidConnectionStore
    {
        public PlaidConnection? Connection { get; set; }

        public Task<PlaidConnection?> GetAsync(string userId) =>
            Task.FromResult(Connection);

        public Task<PlaidConnection?> GetByItemIdAsync(string itemId) =>
            Task.FromResult<PlaidConnection?>(
                Connection?.ItemId == itemId ? Connection : null);

        public Task<IReadOnlyList<PlaidConnection>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<PlaidConnection>>(
                Connection is null ? [] : [Connection]);

        public Task<string?> GetItemIdAsync(string userId) =>
            Task.FromResult(Connection?.ItemId);

        public Task SaveAsync(
            string userId,
            string itemId,
            string accessToken,
            string? transactionCursor) =>
            Task.CompletedTask;

        public Task SetCursorAsync(string userId, string? transactionCursor) =>
            Task.CompletedTask;

        public Task SetWebhookStatusAsync(
            string userId,
            string status,
            string webhookCode,
            DateTimeOffset receivedAt)
        {
            Connection = Connection! with
            {
                Status = status,
                LastWebhookCode = webhookCode,
                LastWebhookAt = receivedAt
            };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string userId) => Task.CompletedTask;
    }

    private sealed class FakePlaidApi : IPlaidApi
    {
        public PlaidWebhookVerificationKey? VerificationKey { get; init; }

        public Task<string> CreateLinkTokenAsync(
            string clientUserId,
            string? accessToken) =>
            Task.FromResult("link-token");

        public Task<PlaidTokenExchangeResult> ExchangePublicTokenAsync(
            string publicToken) =>
            throw new NotSupportedException();

        public Task RemoveItemAsync(string accessToken) =>
            throw new NotSupportedException();

        public Task<AccountsGetResponse> GetAccountsAsync(
            string accessToken) =>
            throw new NotSupportedException();

        public Task<TransactionsSyncResponse> SyncTransactionsAsync(
            string accessToken,
            string? cursor,
            int count) =>
            throw new NotSupportedException();

        public Task<PlaidWebhookVerificationKey>
            GetWebhookVerificationKeyAsync(string keyId) =>
            Task.FromResult(VerificationKey!);
    }
}
