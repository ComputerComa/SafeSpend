using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Identity;

namespace SafeSpend.Web.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    IdentitySetupService setupService) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl());
        }

        if (await setupService.IsSetupRequiredAsync())
        {
            return RedirectToPage("/Account/Setup", new
            {
                ReturnUrl
            });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await setupService.IsSetupRequiredAsync())
        {
            return RedirectToPage("/Account/Setup", new
            {
                ReturnUrl
            });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(GetSafeReturnUrl());
        }

        ModelState.AddModelError(
            string.Empty,
            result.IsLockedOut
                ? "This account is temporarily locked. Try again later."
                : "The email or password is incorrect.");

        return Page();
    }

    private string GetSafeReturnUrl()
    {
        return !string.IsNullOrWhiteSpace(ReturnUrl)
               && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Content("~/")!;
    }

    public sealed class LoginInput
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
