using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Web.Pages;

[Authorize]
public sealed class SetupModel : PageModel
{
    private readonly IForecastScheduleStore _scheduleStore;
    private readonly PlaidLinkService _plaidLinkService;
    private readonly ILogger<SetupModel> _logger;

    public SetupModel(
        IForecastScheduleStore scheduleStore,
        PlaidLinkService plaidLinkService,
        ILogger<SetupModel> logger)
    {
        _scheduleStore = scheduleStore;
        _plaidLinkService = plaidLinkService;
        _logger = logger;
    }

    public PaycheckSchedule? PaycheckSchedule { get; private set; }

    public IReadOnlyList<BillSchedule> BillSchedules
    { get; private set; } = [];

    public IReadOnlyList<PlaidAccountSummary> Accounts
    { get; private set; } = [];

    public bool IsPlaidConnected { get; private set; }

    public string ConnectionStatus { get; private set; } = "Disconnected";

    public string? ScheduleError { get; private set; }

    public string? PlaidError { get; private set; }

    [BindProperty]
    public PaycheckScheduleInput PaycheckInput { get; set; } = new();

    [BindProperty]
    public BillScheduleInput BillInput { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadSetupAsync(populateInputs: true);
    }

    public async Task<IActionResult> OnPostSavePaycheckScheduleAsync()
    {
        if (!TryCreatePaycheckSchedule(out var schedule))
        {
            await LoadSetupAsync(populateInputs: false);
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
            await LoadSetupAsync(populateInputs: false);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddBillAsync()
    {
        if (!TryCreateBillSchedule(out var schedule))
        {
            await LoadSetupAsync(populateInputs: false);
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
            await LoadSetupAsync(populateInputs: false);
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

        try
        {
            await _scheduleStore.DeleteBillScheduleAsync(id);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to delete bill schedule. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "The bill could not be removed.";
            await LoadSetupAsync(populateInputs: true);
            return Page();
        }

        return RedirectToPage();
    }

    private async Task LoadSetupAsync(bool populateInputs)
    {
        IsPlaidConnected = await _plaidLinkService.IsConnectedAsync();
        ConnectionStatus = await _plaidLinkService.GetConnectionStatusAsync();

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
                "Unable to load setup schedules. Error type: {ErrorType}",
                exception.GetType().Name);
            ScheduleError = "Forecast schedules could not be loaded.";
        }

        if (!IsPlaidConnected)
        {
            return;
        }

        try
        {
            Accounts = await _plaidLinkService
                .GetCheckingAndSavingsAccountsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Unable to load setup Plaid accounts. Error type: {ErrorType}",
                exception.GetType().Name);
            PlaidError =
                "Connected account details could not be loaded right now.";
        }
    }

    private void PopulateInputs()
    {
        if (PaycheckSchedule is null)
        {
            return;
        }

        PaycheckInput = new PaycheckScheduleInput
        {
            NextPaycheckDate = PaycheckSchedule.NextPaycheckDate,
            FollowingPaycheckDate = PaycheckSchedule.FollowingPaycheckDate,
            AmountDollars = PaycheckSchedule.AmountCents / 100m,
            CushionDollars = PaycheckSchedule.CushionCents / 100m
        };
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
