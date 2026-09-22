namespace SafeSpend.Web.Services.Forecasting;

public enum BillFrequency
{
    OneTime,
    Weekly,
    Biweekly,
    Monthly
}

public sealed record BillSchedule(
    int Id,
    string Name,
    DateOnly NextDueDate,
    long AmountCents,
    BillFrequency Frequency);

public sealed record PaycheckSchedule(
    DateOnly NextPaycheckDate,
    DateOnly FollowingPaycheckDate,
    long AmountCents,
    long CushionCents);
