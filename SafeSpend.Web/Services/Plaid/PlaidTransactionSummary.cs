namespace SafeSpend.Web.Services.Plaid;

public sealed record PlaidTransactionSummary(
    string TransactionId,
    string AccountId,
    DateOnly? Date,
    decimal? Amount,
    string Name,
    string? MerchantName,
    string? CurrencyCode,
    bool IsPending,
    string? PersonalFinancePrimaryCategory,
    string? PersonalFinanceDetailedCategory);

public sealed record PlaidRemovedTransactionSummary(
    string TransactionId,
    string AccountId);

public sealed record PlaidTransactionSyncResult(
    IReadOnlyList<PlaidTransactionSummary> Added,
    IReadOnlyList<PlaidTransactionSummary> Modified,
    IReadOnlyList<PlaidRemovedTransactionSummary> Removed,
    string? NextCursor);
