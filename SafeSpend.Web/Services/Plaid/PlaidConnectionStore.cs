using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidConnectionStore : IPlaidConnectionStore
{
    public const string ApplicationName = "SafeSpend";
    public const string AccessTokenPurpose =
        "SafeSpend.PlaidAccessToken.v1";

    private readonly IDbContextFactory<SafeSpendDbContext> _contextFactory;
    private readonly ILegacyPlaidAccessTokenProtector _legacyProtector;
    private readonly ILogger<PlaidConnectionStore> _logger;
    private readonly IDataProtector _accessTokenProtector =
        null!;

    public PlaidConnectionStore(
        IDbContextFactory<SafeSpendDbContext> contextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILegacyPlaidAccessTokenProtector legacyProtector,
        ILogger<PlaidConnectionStore> logger)
    {
        _contextFactory = contextFactory;
        _legacyProtector = legacyProtector;
        _logger = logger;
        _accessTokenProtector = dataProtectionProvider.CreateProtector(
            AccessTokenPurpose);
    }

    public async Task<PlaidConnection?> GetAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            return null;
        }

        var accessToken = await UnprotectAsync(context, entity);
        return Map(entity, accessToken);
    }

    public async Task<PlaidConnection?> GetByItemIdAsync(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.ItemId == itemId);

        return entity is null
            ? null
            : Map(entity, await UnprotectAsync(context, entity));
    }

    public async Task<IReadOnlyList<PlaidConnection>> GetAllAsync()
    {
        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entities = await context.PlaidConnections
            .ToListAsync();

        var connections = new List<PlaidConnection>(entities.Count);
        foreach (var entity in entities)
        {
            connections.Add(Map(
                entity,
                await UnprotectAsync(context, entity)));
        }

        return connections;
    }

    public async Task<string?> GetItemIdAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        return await context.PlaidConnections
            .Where(row => row.UserId == userId)
            .Select(row => row.ItemId)
            .SingleOrDefaultAsync();
    }

    public async Task SaveAsync(
        string userId,
        string itemId,
        string accessToken,
        string? transactionCursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            entity = new PlaidConnectionEntity
            {
                UserId = userId,
                ItemId = string.Empty,
                ProtectedAccessToken = string.Empty,
                Status = "Connected"
            };
            context.PlaidConnections.Add(entity);
        }

        entity.ItemId = itemId;
        entity.ProtectedAccessToken = _accessTokenProtector.Protect(
            accessToken);
        entity.TransactionCursor = transactionCursor;
        entity.Status = "Connected";
        entity.LastWebhookCode = null;
        entity.LastWebhookAt = null;

        await context.SaveChangesAsync();
    }

    public async Task SetCursorAsync(
        string userId,
        string? transactionCursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            throw new InvalidOperationException(
                "A Plaid connection is required.");
        }

        entity.TransactionCursor = transactionCursor;
        await context.SaveChangesAsync();
    }

    public async Task SetWebhookStatusAsync(
        string userId,
        string status,
        string webhookCode,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookCode);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            return;
        }

        entity.Status = status;
        entity.LastWebhookCode = webhookCode;
        entity.LastWebhookAt = receivedAt;
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var context =
            await _contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            return;
        }

        context.PlaidConnections.Remove(entity);
        await context.SaveChangesAsync();
    }

    private async Task<string> UnprotectAsync(
        SafeSpendDbContext context,
        PlaidConnectionEntity entity)
    {
        try
        {
            return _accessTokenProtector.Unprotect(
                entity.ProtectedAccessToken);
        }
        catch (Exception exception) when (
            exception is CryptographicException or ArgumentException)
        {
            if (_legacyProtector.TryUnprotect(
                    entity.ProtectedAccessToken,
                    out var legacyAccessToken))
            {
                entity.ProtectedAccessToken = _accessTokenProtector.Protect(
                    legacyAccessToken);
                await context.SaveChangesAsync();
                _logger.LogInformation(
                    "Recovered and re-protected a Plaid connection using " +
                    "legacy Data Protection configuration.");
                return legacyAccessToken;
            }

            _logger.LogWarning(
                "Unable to recover a Plaid connection after trying " +
                "{CandidateCount} legacy Data Protection combinations " +
                "across {KeyDirectoryCount} key directories.",
                _legacyProtector.CandidateCount,
                _legacyProtector.KeyDirectoryCount);

            throw new PlaidConnectionUnavailableException(
                "The stored Plaid connection could not be unlocked.",
                exception);
        }
    }

    private static PlaidConnection Map(
        PlaidConnectionEntity entity,
        string accessToken)
    {
        return new PlaidConnection(
            entity.UserId,
            entity.ItemId,
            accessToken,
            entity.TransactionCursor,
            entity.Status,
            entity.LastWebhookCode,
            entity.LastWebhookAt);
    }
}
