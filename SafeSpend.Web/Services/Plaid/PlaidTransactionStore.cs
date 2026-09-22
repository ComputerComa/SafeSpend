using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidTransactionStore(
    IDbContextFactory<SafeSpendDbContext> contextFactory)
    : IPlaidTransactionStore
{
    public async Task<string?> GetCursorAsync(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        await using var context =
            await contextFactory.CreateDbContextAsync();

        var item = await context.PlaidItems
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.ItemId == itemId);

        return item?.TransactionCursor;
    }

    public async Task ApplySyncAsync(
        string itemId,
        PlaidTransactionSyncResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(result);

        await using var context =
            await contextFactory.CreateDbContextAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync();

        var item = await context.PlaidItems
            .SingleOrDefaultAsync(row => row.ItemId == itemId);

        if (item is null)
        {
            item = new PlaidItemEntity
            {
                ItemId = itemId
            };
            context.PlaidItems.Add(item);
        }

        item.TransactionCursor = result.NextCursor;

        foreach (var plaidTransaction in result.Added.Concat(result.Modified))
        {
            var entity = await context.PlaidTransactions
                .SingleOrDefaultAsync(row =>
                    row.ItemId == itemId &&
                    row.TransactionId == plaidTransaction.TransactionId);

            if (entity is null)
            {
                context.PlaidTransactions.Add(
                    CreateEntity(itemId, plaidTransaction));
            }
            else
            {
                UpdateEntity(entity, plaidTransaction);
            }
        }

        foreach (var removedTransaction in result.Removed)
        {
            var entity = await context.PlaidTransactions
                .SingleOrDefaultAsync(row =>
                    row.ItemId == itemId &&
                    row.TransactionId == removedTransaction.TransactionId);

            if (entity is not null)
            {
                context.PlaidTransactions.Remove(entity);
            }
        }

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<PlaidTransactionSummary>>
        GetTransactionsAsync(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        await using var context =
            await contextFactory.CreateDbContextAsync();

        var transactions = await context.PlaidTransactions
            .AsNoTracking()
            .Where(row => row.ItemId == itemId)
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.TransactionId)
            .ToListAsync();

        return transactions
            .Select(CreateSummary)
            .ToArray();
    }

    private static PlaidTransactionEntity CreateEntity(
        string itemId,
        PlaidTransactionSummary transaction)
    {
        var entity = new PlaidTransactionEntity
        {
            ItemId = itemId,
            TransactionId = transaction.TransactionId,
            AccountId = string.Empty,
            Name = string.Empty
        };

        UpdateEntity(entity, transaction);
        return entity;
    }

    private static void UpdateEntity(
        PlaidTransactionEntity entity,
        PlaidTransactionSummary transaction)
    {
        entity.AccountId = transaction.AccountId;
        entity.Date = transaction.Date;
        entity.Amount = transaction.Amount;
        entity.Name = transaction.Name;
        entity.MerchantName = transaction.MerchantName;
        entity.CurrencyCode = transaction.CurrencyCode;
        entity.IsPending = transaction.IsPending;
        entity.PersonalFinancePrimaryCategory =
            transaction.PersonalFinancePrimaryCategory;
        entity.PersonalFinanceDetailedCategory =
            transaction.PersonalFinanceDetailedCategory;
    }

    private static PlaidTransactionSummary CreateSummary(
        PlaidTransactionEntity entity)
    {
        return new PlaidTransactionSummary(
            entity.TransactionId,
            entity.AccountId,
            entity.Date,
            entity.Amount,
            entity.Name,
            entity.MerchantName,
            entity.CurrencyCode,
            entity.IsPending,
            entity.PersonalFinancePrimaryCategory,
            entity.PersonalFinanceDetailedCategory);
    }
}
