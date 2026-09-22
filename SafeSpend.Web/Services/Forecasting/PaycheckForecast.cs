namespace SafeSpend.Web.Services.Forecasting;

public sealed record PaycheckForecastInput(
    DateOnly AsOfDate,
    long AvailableBalanceCents,
    long CushionCents,
    DateOnly NextPaycheckDate,
    DateOnly FollowingPaycheckDate,
    long NextPaycheckAmountCents,
    IReadOnlyCollection<CashFlowOccurrence> Occurrences);

public sealed record PaycheckForecast(
    long SafeToSpendNowCents,
    long AvailableFromNextPaycheckCents,
    long ProjectedBalanceBeforeNextPaycheckCents,
    long BillsBeforeNextPaycheckCents,
    long BillsInNextPayPeriodCents);
