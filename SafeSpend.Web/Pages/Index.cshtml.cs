using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Web.Pages;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly PaycheckForecastService _forecastService;
    private readonly CashFlowScheduleService _scheduleService;
    private readonly IForecastScheduleStore _scheduleStore;
    private readonly PlaidLinkService _plaidLinkService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        PaycheckForecastService forecastService,
        CashFlowScheduleService scheduleService,
        IForecastScheduleStore scheduleStore,
        PlaidLinkService plaidLinkService,
        ILogger<IndexModel> logger)
    {
        _forecastService = forecastService;
        _scheduleService = scheduleService;
        _scheduleStore = scheduleStore;
        _plaidLinkService = plaidLinkService;
        _logger = logger;
    }

    public PaycheckForecast? Forecast { get; private set; }

    public PaycheckSchedule? PaycheckSchedule { get; private set; }

    public IReadOnlyList<BillSchedule> BillSchedules
    { get; private set; } = [];

    public bool IsPlaidConnected { get; private set; }

    public IReadOnlyList<PlaidTransactionSummary> RecentTransactions
    { get; private set; } = [];

    public IReadOnlyList<PlaidAccountSummary> ConnectedAccounts
    { get; private set; } = [];

    public string? PlaidDataError { get; private set; }

    public string? ForecastError { get; private set; }

    public string? ScheduleError { get; private set; }

    public DateOnly? NextPaycheckDate =>
        PaycheckSchedule?.NextPaycheckDate;

    public long? CurrentBalanceCents { get; private set; }

    public IReadOnlyList<CashFlowOccurrence> UpcomingOccurrences
    { get; private set; } = [];

    public long UpcomingTotalCents =>
        UpcomingOccurrences.Sum(occurrence => occurrence.AmountCents);

    public bool NeedsSetup =>
        !IsPlaidConnected || PaycheckSchedule is null;

    public string SetupMessage
    {
        get
        {
            if (!IsPlaidConnected && PaycheckSchedule is null)
            {
                return "Connect a bank and add your paycheck schedule to start your forecast.";
            }

            if (!IsPlaidConnected)
            {
                return "Connect a bank to calculate your forecast from your current balance.";
            }

            return "Add your paycheck schedule to calculate your forecast.";
        }
    }

    public async Task OnGetAsync()
    {
        await LoadDashboardAsync();
    }

    public async Task<IActionResult> OnPostSyncTransactionsAsync()
    {
        try
        {
            await _plaidLinkService.SyncTransactionsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to sync Plaid transactions from the dashboard. Error type: {ErrorType}",
                exception.GetType().Name);
            PlaidDataError =
                "Transactions could not be synced right now.";
            await LoadDashboardAsync();
            return Page();
        }

        return RedirectToPage();
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            PaycheckSchedule = await _scheduleStore
                .GetPaycheckScheduleAsync();
            BillSchedules = await _scheduleStore
                .GetBillSchedulesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to load forecast schedules. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "Forecast schedules could not be loaded.";
            return;
        }

        IsPlaidConnected = await _plaidLinkService.IsConnectedAsync();

        if (IsPlaidConnected)
        {
            await LoadPlaidDataAsync();
            TryGetAvailableBalanceCents(out _);
        }

        if (PaycheckSchedule is null)
        {
            ForecastError =
                "Add your paycheck schedule to calculate a forecast.";
            return;
        }

        if (!IsPlaidConnected)
        {
            ForecastError =
                "Connect a bank to calculate a forecast from your balance.";
            return;
        }

        if (!TryGetAvailableBalanceCents(out var availableBalanceCents))
        {
            ForecastError =
                "A connected account balance is required to calculate a forecast.";
            return;
        }

        try
        {
            UpcomingOccurrences = _scheduleService.BuildOccurrences(
                BillSchedules,
                PaycheckSchedule.NextPaycheckDate,
                PaycheckSchedule.FollowingPaycheckDate);

            Forecast = _forecastService.Calculate(
                new PaycheckForecastInput(
                    AsOfDate: DateOnly.FromDateTime(DateTime.Today),
                    AvailableBalanceCents: availableBalanceCents,
                    CushionCents: PaycheckSchedule.CushionCents,
                    NextPaycheckDate: PaycheckSchedule.NextPaycheckDate,
                    FollowingPaycheckDate:
                        PaycheckSchedule.FollowingPaycheckDate,
                    NextPaycheckAmountCents: PaycheckSchedule.AmountCents,
                    Occurrences: UpcomingOccurrences));
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to calculate forecast. Error type: {ErrorType}",
                exception.GetType().Name);
            ForecastError = "The forecast could not be calculated.";
        }
    }

    private async Task LoadPlaidDataAsync()
    {
        try
        {
            RecentTransactions = (await _plaidLinkService
                .GetStoredTransactionsAsync())
                .Take(10)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to load stored Plaid transactions. Error type: {ErrorType}",
                exception.GetType().Name);
            PlaidDataError =
                "Stored transactions could not be loaded right now.";
        }

        try
        {
            ConnectedAccounts = await _plaidLinkService
                .GetCheckingAndSavingsAccountsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to load Plaid account balances. Error type: {ErrorType}",
                exception.GetType().Name);
            PlaidDataError =
                "Connected account balances could not be loaded right now.";
        }
    }

    private bool TryGetAvailableBalanceCents(out long balanceCents)
    {
        var balances = ConnectedAccounts
            .Select(account =>
                account.CurrentBalance ?? account.AvailableBalance)
            .ToArray();

        if (balances.Length == 0 || balances.Any(balance => !balance.HasValue))
        {
            balanceCents = 0;
            return false;
        }

        var total = balances.Sum(balance => balance!.Value);
        balanceCents = checked((long)Math.Round(
            total * 100m,
            MidpointRounding.AwayFromZero));
        CurrentBalanceCents = balanceCents;
        return true;
    }

    public static string FormatMoney(long cents)
    {
        return (cents / 100m).ToString("C");
    }

    public static string FormatFrequency(BillFrequency frequency)
    {
        return frequency switch
        {
            BillFrequency.OneTime => "One time",
            BillFrequency.Biweekly => "Every two weeks",
            _ => frequency.ToString()
        };
    }

    public static string FormatTransactionAmount(decimal? amount)
    {
        if (!amount.HasValue)
        {
            return "—";
        }

        var sign = amount.Value switch
        {
            > 0 => "-",
            < 0 => "+",
            _ => string.Empty
        };

        return $"{sign}{Math.Abs(amount.Value):C}";
    }
}
