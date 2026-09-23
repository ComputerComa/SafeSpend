using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SafeSpend.Web.Services.Identity;

namespace SafeSpend.Web.Pages.Account;

[AllowAnonymous]
public sealed class SetupModel(
    IdentitySetupService setupService,
    SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public SetupInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await setupService.IsSetupRequiredAsync())
        {
            return RedirectToPage("/Account/Login", new
            {
                ReturnUrl
            });
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await setupService.IsSetupRequiredAsync())
        {
            return RedirectToPage("/Account/Login", new
            {
                ReturnUrl
            });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await setupService.CreateFirstAdministratorAsync(
            Input.Email,
            Input.DisplayName,
            Input.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        var signInResult = await signInManager.PasswordSignInAsync(
            Input.Email,
            Input.Password,
            isPersistent: false,
            lockoutOnFailure: false);

        if (!signInResult.Succeeded)
        {
            return RedirectToPage("/Account/Login", new
            {
                ReturnUrl
            });
        }

        return LocalRedirect(GetSafeReturnUrl());
    }

    private string GetSafeReturnUrl()
    {
        return !string.IsNullOrWhiteSpace(ReturnUrl)
               && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Content("~/")!;
    }

    public sealed class SetupInput
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Display name")]
        public string? DisplayName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password))]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
