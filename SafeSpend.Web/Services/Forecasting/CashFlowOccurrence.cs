namespace SafeSpend.Web.Services.Forecasting;

public enum CashFlowType
{
    Income,
    Expense
}

public sealed record CashFlowOccurrence(
    string Name,
    DateOnly Date,
    long AmountCents,
    CashFlowType Type,
    bool IsSettled = false)
{
    public long SignedAmountCents =>
        Type == CashFlowType.Income
            ? AmountCents
            : -AmountCents;
}
