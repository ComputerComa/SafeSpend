using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Forecasting;

namespace SafeSpend.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly PaycheckForecastService _forecastService;

    public IndexModel(PaycheckForecastService forecastService)
    {
        _forecastService = forecastService;
    }

    public PaycheckForecast Forecast { get; private set; } = null!;

    public DateOnly NextPaycheckDate { get; private set; }

    public IReadOnlyList<CashFlowOccurrence> UpcomingOccurrences
    { get; private set; } = [];

    public void OnGet()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Temporary demo dates until schedules are stored in the database.
        NextPaycheckDate = today.AddDays(8);
        var followingPaycheckDate = NextPaycheckDate.AddDays(14);

        UpcomingOccurrences =
        [
            new(
                "Electric",
                NextPaycheckDate.AddDays(1),
                19_500,
                CashFlowType.Expense),

            new(
                "Internet",
                NextPaycheckDate.AddDays(3),
                13_300,
                CashFlowType.Expense),

            new(
                "Phone",
                NextPaycheckDate.AddDays(5),
                12_500,
                CashFlowType.Expense)
        ];

        Forecast = _forecastService.Calculate(
            new PaycheckForecastInput(
                AsOfDate: today,
                AvailableBalanceCents: -6_631,
                CushionCents: 10_000,
                NextPaycheckDate: NextPaycheckDate,
                FollowingPaycheckDate: followingPaycheckDate,
                NextPaycheckAmountCents: 135_000,
                Occurrences: UpcomingOccurrences));
    }

    public static string FormatMoney(long cents)
    {
        return (cents / 100m).ToString("C");
    }
}
