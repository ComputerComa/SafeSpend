using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Web.Pages.Plaid;

public sealed class ConnectModel(
    PlaidLinkService plaidLinkService,
    PlaidConnectionState connectionState,
    ILogger<ConnectModel> logger) : PageModel
{
    public IReadOnlyList<PlaidAccountSummary> Accounts { get; private set; } = [];

    public string? AccountLoadError { get; private set; }

    public bool IsConnected => connectionState.IsConnected;

    public string? ItemId => connectionState.ItemId;

    public async Task OnGetAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            Accounts = await plaidLinkService
                .GetCheckingAndSavingsAccountsAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Unable to load Plaid accounts. Error type: {ErrorType}",
                exception.GetType().Name);

            AccountLoadError =
                "The connected accounts could not be loaded right now.";
        }
    }

    public async Task<IActionResult> OnPostCreateLinkTokenAsync()
    {
        try
        {
            var linkToken =
                await plaidLinkService.CreateLinkTokenAsync();

            return new JsonResult(new
            {
                linkToken
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Unable to create a Plaid Link token. Error type: {ErrorType}",
                exception.GetType().Name);

            return BadRequest(new
            {
                error = "The Plaid Link session could not be created."
            });
        }
    }

    public async Task<IActionResult> OnPostExchangePublicTokenAsync(
        [FromBody] ExchangePublicTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PublicToken))
        {
            return BadRequest(new
            {
                error = "A public token is required."
            });
        }

        try
        {
            var itemId =
                await plaidLinkService.ExchangePublicTokenAsync(
                    request.PublicToken);

            return new JsonResult(new
            {
                itemId
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Unable to exchange the Plaid public token. Error type: {ErrorType}",
                exception.GetType().Name);

            return BadRequest(new
            {
                error = "The Plaid connection could not be completed."
            });
        }
    }

    public async Task<IActionResult> OnPostSyncTransactionsAsync()
    {
        try
        {
            var result = await plaidLinkService.SyncTransactionsAsync();

            return new JsonResult(new
            {
                added = result.Added.Count,
                modified = result.Modified.Count,
                removed = result.Removed.Count
            });
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Unable to sync Plaid transactions. Error type: {ErrorType}",
                exception.GetType().Name);

            return BadRequest(new
            {
                error = "Transactions could not be synced right now."
            });
        }
    }

    public sealed record ExchangePublicTokenRequest(
        string PublicToken);
}
