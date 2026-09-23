using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Identity;

namespace SafeSpend.Web.Pages.Account;

[Authorize]
public sealed class LogoutModel(
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }

    public IActionResult OnGet()
    {
        return RedirectToPage("/Account/Login");
    }
}
