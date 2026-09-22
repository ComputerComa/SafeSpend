using SafeSpend.Web.Services.Forecasting;

namespace SafeSpend.Tests.Services.Forecasting;

public sealed class CashFlowScheduleServiceTests
{
    [Fact]
    public void BuildOccurrences_AdvancesRecurringBillsIntoForecastWindow()
    {
        var service = new CashFlowScheduleService();

        var occurrences = service.BuildOccurrences(
            schedules:
            [
                new BillSchedule(
                    1,
                    "Rent",
                    new DateOnly(2026, 9, 1),
                    150_000,
                    BillFrequency.Monthly),
                new BillSchedule(
                    2,
                    "Insurance",
                    new DateOnly(2026, 9, 25),
                    15_000,
                    BillFrequency.OneTime)
            ],
            fromDate: new DateOnly(2026, 9, 22),
            throughDate: new DateOnly(2026, 10, 14));

        Assert.Collection(
            occurrences,
            occurrence =>
            {
                Assert.Equal("Insurance", occurrence.Name);
                Assert.Equal(new DateOnly(2026, 9, 25), occurrence.Date);
            },
            occurrence =>
            {
                Assert.Equal("Rent", occurrence.Name);
                Assert.Equal(new DateOnly(2026, 10, 1), occurrence.Date);
            });
    }
}
