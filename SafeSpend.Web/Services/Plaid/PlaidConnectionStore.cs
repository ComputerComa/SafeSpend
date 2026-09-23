using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidConnectionStore(
    IDbContextFactory<SafeSpendDbContext> contextFactory,
    IDataProtectionProvider dataProtectionProvider)
    : IPlaidConnectionStore
{
    private readonly IDataProtector _accessTokenProtector =
        dataProtectionProvider.CreateProtector(
            "SafeSpend.PlaidAccessToken.v1");

    public async Task<PlaidConnection?> GetAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var context =
            await contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            return null;
        }

        string accessToken;
        try
        {
            accessToken = _accessTokenProtector.Unprotect(
                entity.ProtectedAccessToken);
        }
        catch (Exception exception) when (
            exception is CryptographicException ||
            exception is ArgumentException)
        {
            throw new InvalidOperationException(
                "The stored Plaid connection could not be unlocked.",
                exception);
        }

        return Map(entity, accessToken);
    }

    public async Task<PlaidConnection?> GetByItemIdAsync(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        await using var context =
            await contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.ItemId == itemId);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<PlaidConnection>> GetAllAsync()
    {
        await using var context =
            await contextFactory.CreateDbContextAsync();
        var entities = await context.PlaidConnections
            .AsNoTracking()
            .ToListAsync();

        return entities.Select(Map).ToArray();
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
            await contextFactory.CreateDbContextAsync();
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
            await contextFactory.CreateDbContextAsync();
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
            await contextFactory.CreateDbContextAsync();
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
            await contextFactory.CreateDbContextAsync();
        var entity = await context.PlaidConnections
            .SingleOrDefaultAsync(row => row.UserId == userId);

        if (entity is null)
        {
            return;
        }

        context.PlaidConnections.Remove(entity);
        await context.SaveChangesAsync();
    }

    private PlaidConnection Map(PlaidConnectionEntity entity)
    {
        string accessToken;
        try
        {
            accessToken = _accessTokenProtector.Unprotect(
                entity.ProtectedAccessToken);
        }
        catch (Exception exception) when (
            exception is CryptographicException ||
            exception is ArgumentException)
        {
            throw new InvalidOperationException(
                "The stored Plaid connection could not be unlocked.",
                exception);
        }

        return Map(entity, accessToken);
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
