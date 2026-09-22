using SafeSpend.Web.Services.Forecasting;

namespace SafeSpend.Tests.Services.Forecasting;

public sealed class PaycheckForecastServiceTests
{
    private readonly PaycheckForecastService _service = new();

    [Fact]
    public void Calculate_ProducesExpectedPaycheckPlan()
    {
        var input = new PaycheckForecastInput(
            AsOfDate: new DateOnly(2026, 9, 22),
            AvailableBalanceCents: -6_631,
            CushionCents: 10_000,
            NextPaycheckDate: new DateOnly(2026, 9, 30),
            FollowingPaycheckDate: new DateOnly(2026, 10, 14),
            NextPaycheckAmountCents: 135_000,
            Occurrences:
            [
                new(
                    "Electric",
                    new DateOnly(2026, 10, 1),
                    19_500,
                    CashFlowType.Expense),

                new(
                    "Internet",
                    new DateOnly(2026, 10, 3),
                    13_300,
                    CashFlowType.Expense),

                new(
                    "Phone",
                    new DateOnly(2026, 10, 5),
                    12_500,
                    CashFlowType.Expense)
            ]);

        var result = _service.Calculate(input);

        Assert.Equal(-16_631L, result.SafeToSpendNowCents);
        Assert.Equal(73_069L, result.AvailableFromNextPaycheckCents);
        Assert.Equal(-6_631L,
            result.ProjectedBalanceBeforeNextPaycheckCents);

        Assert.Equal(0L, result.BillsBeforeNextPaycheckCents);
        Assert.Equal(45_300L, result.BillsInNextPayPeriodCents);
    }

    [Fact]
    public void Calculate_SubtractsBillsDueBeforeNextPaycheck()
    {
        var input = new PaycheckForecastInput(
            AsOfDate: new DateOnly(2026, 9, 22),
            AvailableBalanceCents: 50_000,
            CushionCents: 10_000,
            NextPaycheckDate: new DateOnly(2026, 9, 30),
            FollowingPaycheckDate: new DateOnly(2026, 10, 14),
            NextPaycheckAmountCents: 135_000,
            Occurrences:
            [
                new(
                    "Insurance",
                    new DateOnly(2026, 9, 25),
                    15_000,
                    CashFlowType.Expense)
            ]);

        var result = _service.Calculate(input);

        Assert.Equal(35_000L,
            result.ProjectedBalanceBeforeNextPaycheckCents);

        Assert.Equal(25_000L, result.SafeToSpendNowCents);
        Assert.Equal(160_000L,
            result.AvailableFromNextPaycheckCents);

        Assert.Equal(15_000L,
            result.BillsBeforeNextPaycheckCents);
    }

    [Fact]
    public void Calculate_IgnoresSettledOccurrences()
    {
        var input = new PaycheckForecastInput(
            AsOfDate: new DateOnly(2026, 9, 22),
            AvailableBalanceCents: 50_000,
            CushionCents: 10_000,
            NextPaycheckDate: new DateOnly(2026, 9, 30),
            FollowingPaycheckDate: new DateOnly(2026, 10, 14),
            NextPaycheckAmountCents: 135_000,
            Occurrences:
            [
                new(
                    "Already paid bill",
                    new DateOnly(2026, 9, 25),
                    15_000,
                    CashFlowType.Expense,
                    IsSettled: true)
            ]);

        var result = _service.Calculate(input);

        Assert.Equal(40_000L, result.SafeToSpendNowCents);
        Assert.Equal(0L, result.BillsBeforeNextPaycheckCents);
    }

    [Fact]
    public void Calculate_RejectsInvalidPaycheckWindow()
    {
        var input = new PaycheckForecastInput(
            AsOfDate: new DateOnly(2026, 9, 22),
            AvailableBalanceCents: 0,
            CushionCents: 0,
            NextPaycheckDate: new DateOnly(2026, 9, 30),
            FollowingPaycheckDate: new DateOnly(2026, 9, 30),
            NextPaycheckAmountCents: 135_000,
            Occurrences: []);

        Assert.Throws<ArgumentException>(
            () => _service.Calculate(input));
    }
}
