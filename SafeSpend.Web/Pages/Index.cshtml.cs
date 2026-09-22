using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly PaycheckForecastService _forecastService;
    private readonly CashFlowScheduleService _scheduleService;
    private readonly IForecastScheduleStore _scheduleStore;
    private readonly PlaidLinkService _plaidLinkService;
    private readonly PlaidConnectionState _connectionState;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        PaycheckForecastService forecastService,
        CashFlowScheduleService scheduleService,
        IForecastScheduleStore scheduleStore,
        PlaidLinkService plaidLinkService,
        PlaidConnectionState connectionState,
        ILogger<IndexModel> logger)
    {
        _forecastService = forecastService;
        _scheduleService = scheduleService;
        _scheduleStore = scheduleStore;
        _plaidLinkService = plaidLinkService;
        _connectionState = connectionState;
        _logger = logger;
    }

    public PaycheckForecast? Forecast { get; private set; }

    public PaycheckSchedule? PaycheckSchedule { get; private set; }

    public IReadOnlyList<BillSchedule> BillSchedules
    { get; private set; } = [];

    public bool IsPlaidConnected => _connectionState.IsConnected;

    public IReadOnlyList<PlaidTransactionSummary> RecentTransactions
    { get; private set; } = [];

    public string? PlaidDataError { get; private set; }

    public string? ForecastError { get; private set; }

    public string? ScheduleError { get; private set; }

    public DateOnly? NextPaycheckDate =>
        PaycheckSchedule?.NextPaycheckDate;

    public IReadOnlyList<CashFlowOccurrence> UpcomingOccurrences
    { get; private set; } = [];

    [BindProperty]
    public PaycheckScheduleInput PaycheckInput { get; set; } = new();

    [BindProperty]
    public BillScheduleInput BillInput { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadDashboardAsync(populateInputs: true);
    }

    public async Task<IActionResult> OnPostSavePaycheckScheduleAsync()
    {
        if (!TryCreatePaycheckSchedule(out var schedule))
        {
            await LoadDashboardAsync(populateInputs: false);
            return Page();
        }

        try
        {
            await _scheduleStore.SavePaycheckScheduleAsync(schedule);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to save paycheck schedule. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "The paycheck schedule could not be saved.";
            await LoadDashboardAsync(populateInputs: false);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddBillAsync()
    {
        if (!TryCreateBillSchedule(out var schedule))
        {
            await LoadDashboardAsync(populateInputs: false);
            return Page();
        }

        try
        {
            await _scheduleStore.AddBillScheduleAsync(schedule);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to save bill schedule. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "The bill could not be saved.";
            await LoadDashboardAsync(populateInputs: false);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteBillAsync(int id)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        await _scheduleStore.DeleteBillScheduleAsync(id);
        return RedirectToPage();
    }

    private async Task LoadDashboardAsync(bool populateInputs)
    {
        try
        {
            PaycheckSchedule = await _scheduleStore
                .GetPaycheckScheduleAsync();
            BillSchedules = await _scheduleStore
                .GetBillSchedulesAsync();

            if (populateInputs)
            {
                PopulateInputs();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to load forecast schedules. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "Forecast schedules could not be loaded.";
            return;
        }

        if (IsPlaidConnected)
        {
            await LoadPlaidDataAsync();
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

    public IReadOnlyList<PlaidAccountSummary> ConnectedAccounts
    { get; private set; } = [];

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
        return true;
    }

    private void PopulateInputs()
    {
        if (PaycheckSchedule is not null)
        {
            PaycheckInput = new PaycheckScheduleInput
            {
                NextPaycheckDate = PaycheckSchedule.NextPaycheckDate,
                FollowingPaycheckDate = PaycheckSchedule.FollowingPaycheckDate,
                AmountDollars = PaycheckSchedule.AmountCents / 100m,
                CushionDollars = PaycheckSchedule.CushionCents / 100m
            };
        }
    }

    private bool TryCreatePaycheckSchedule(
        out PaycheckSchedule schedule)
    {
        schedule = null!;
        var valid = true;

        if (!PaycheckInput.NextPaycheckDate.HasValue)
        {
            ModelState.AddModelError(
                "PaycheckInput.NextPaycheckDate",
                "Enter the next paycheck date.");
            valid = false;
        }

        if (!PaycheckInput.FollowingPaycheckDate.HasValue)
        {
            ModelState.AddModelError(
                "PaycheckInput.FollowingPaycheckDate",
                "Enter the following paycheck date.");
            valid = false;
        }

        if (PaycheckInput.NextPaycheckDate.HasValue &&
            PaycheckInput.FollowingPaycheckDate.HasValue &&
            PaycheckInput.FollowingPaycheckDate <=
            PaycheckInput.NextPaycheckDate)
        {
            ModelState.AddModelError(
                "PaycheckInput.FollowingPaycheckDate",
                "The following paycheck must be after the next paycheck.");
            valid = false;
        }

        if (!TryConvertDollarsToCents(
                PaycheckInput.AmountDollars,
                allowZero: false,
                out var amountCents))
        {
            ModelState.AddModelError(
                "PaycheckInput.AmountDollars",
                "Enter a paycheck amount with at most two decimal places.");
            valid = false;
        }

        if (!TryConvertDollarsToCents(
                PaycheckInput.CushionDollars,
                allowZero: true,
                out var cushionCents))
        {
            ModelState.AddModelError(
                "PaycheckInput.CushionDollars",
                "Enter a safety cushion with at most two decimal places.");
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        schedule = new PaycheckSchedule(
            PaycheckInput.NextPaycheckDate!.Value,
            PaycheckInput.FollowingPaycheckDate!.Value,
            amountCents,
            cushionCents);
        return true;
    }

    private bool TryCreateBillSchedule(out BillSchedule schedule)
    {
        schedule = null!;
        var valid = true;

        if (string.IsNullOrWhiteSpace(BillInput.Name))
        {
            ModelState.AddModelError(
                "BillInput.Name",
                "Enter a bill name.");
            valid = false;
        }

        if (!BillInput.NextDueDate.HasValue)
        {
            ModelState.AddModelError(
                "BillInput.NextDueDate",
                "Enter the next due date.");
            valid = false;
        }

        if (!TryConvertDollarsToCents(
                BillInput.AmountDollars,
                allowZero: false,
                out var amountCents))
        {
            ModelState.AddModelError(
                "BillInput.AmountDollars",
                "Enter a bill amount with at most two decimal places.");
            valid = false;
        }

        if (!Enum.IsDefined(BillInput.Frequency))
        {
            ModelState.AddModelError(
                "BillInput.Frequency",
                "Choose a bill frequency.");
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        schedule = new BillSchedule(
            0,
            BillInput.Name.Trim(),
            BillInput.NextDueDate!.Value,
            amountCents,
            BillInput.Frequency);
        return true;
    }

    private static bool TryConvertDollarsToCents(
        decimal? dollars,
        bool allowZero,
        out long cents)
    {
        cents = 0;

        if (!dollars.HasValue ||
            (allowZero ? dollars.Value < 0 : dollars.Value <= 0))
        {
            return false;
        }

        var value = dollars.Value * 100m;
        if (value != decimal.Truncate(value) ||
            value > long.MaxValue)
        {
            return false;
        }

        cents = (long)value;
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

    public sealed class PaycheckScheduleInput
    {
        public DateOnly? NextPaycheckDate { get; set; }

        public DateOnly? FollowingPaycheckDate { get; set; }

        public decimal? AmountDollars { get; set; }

        public decimal? CushionDollars { get; set; }
    }

    public sealed class BillScheduleInput
    {
        public string Name { get; set; } = string.Empty;

        public DateOnly? NextDueDate { get; set; }

        public decimal? AmountDollars { get; set; }

        public BillFrequency Frequency { get; set; } = BillFrequency.Monthly;
    }
}
