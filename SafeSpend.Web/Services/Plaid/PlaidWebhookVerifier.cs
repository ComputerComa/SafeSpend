using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SafeSpend.Web.Services.Plaid;

public sealed record PlaidWebhookPayload(
    string WebhookType,
    string WebhookCode,
    string? ItemId);

public interface IPlaidWebhookVerifier
{
    Task<PlaidWebhookPayload?> VerifyAsync(
        ReadOnlyMemory<byte> body,
        string? verificationHeader,
        CancellationToken cancellationToken);
}

public sealed class PlaidWebhookVerifier(
    IPlaidApi plaidApi) : IPlaidWebhookVerifier
{
    private static readonly TimeSpan MaximumTokenAge = TimeSpan.FromMinutes(5);

    public async Task<PlaidWebhookPayload?> VerifyAsync(
        ReadOnlyMemory<byte> body,
        string? verificationHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(verificationHeader))
        {
            return null;
        }

        var segments = verificationHeader.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        try
        {
            using var header = ParseJson(DecodeBase64Url(segments[0]));
            using var token = ParseJson(DecodeBase64Url(segments[1]));

            if (!string.Equals(
                    header.RootElement.GetProperty("alg").GetString(),
                    "ES256",
                    StringComparison.Ordinal))
            {
                return null;
            }

            var keyId = header.RootElement.GetProperty("kid").GetString();
            var requestHash = token.RootElement
                .GetProperty("request_body_sha256")
                .GetString();
            var issuedAt = token.RootElement
                .GetProperty("iat")
                .GetInt64();

            if (string.IsNullOrWhiteSpace(keyId) ||
                string.IsNullOrWhiteSpace(requestHash))
            {
                return null;
            }

            var issuedAtTime = DateTimeOffset.FromUnixTimeSeconds(issuedAt);
            if (DateTimeOffset.UtcNow - issuedAtTime > MaximumTokenAge ||
                issuedAtTime - DateTimeOffset.UtcNow > MaximumTokenAge)
            {
                return null;
            }

            var actualHash = Convert.ToHexString(
                SHA256.HashData(body.Span));
            requestHash = requestHash.ToUpperInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(requestHash)))
            {
                return null;
            }

            var key = await plaidApi.GetWebhookVerificationKeyAsync(keyId);
            if (!string.Equals(key.KeyId, keyId, StringComparison.Ordinal) ||
                !string.Equals(key.Algorithm, "ES256", StringComparison.Ordinal) ||
                !string.Equals(key.Curve, "P-256", StringComparison.Ordinal) ||
                key.ExpiresAt is not null && key.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            var signature = DecodeBase64Url(segments[2]);
            if (signature.Length != 64)
            {
                return null;
            }

            var x = DecodeBase64Url(key.X);
            var y = DecodeBase64Url(key.Y);
            using var ecdsa = ECDsa.Create(
                new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = x,
                        Y = y
                    }
                });

            var signedBytes = Encoding.ASCII.GetBytes(
                $"{segments[0]}.{segments[1]}");
            if (!ecdsa.VerifyData(
                    signedBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return null;
            }

            var webhookType = token.RootElement
                .GetProperty("webhook_type")
                .GetString();
            var webhookCode = token.RootElement
                .GetProperty("webhook_code")
                .GetString();

            if (string.IsNullOrWhiteSpace(webhookType) ||
                string.IsNullOrWhiteSpace(webhookCode))
            {
                return null;
            }

            return new PlaidWebhookPayload(
                webhookType,
                webhookCode,
                token.RootElement.TryGetProperty("item_id", out var itemId)
                    ? itemId.GetString()
                    : null);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static JsonDocument ParseJson(byte[] bytes) =>
        JsonDocument.Parse(bytes);

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(
            normalized.Length + (4 - normalized.Length % 4) % 4,
            '=');
        return Convert.FromBase64String(normalized);
    }

}
