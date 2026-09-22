namespace SafeSpend.Web.Services.Plaid;

public sealed record PlaidAccountSummary(
    string AccountId,
    string Name,
    string? OfficialName,
    string? Mask,
    string Type,
    string Subtype,
    decimal? AvailableBalance,
    decimal? CurrentBalance,
    string CurrencyCode);
