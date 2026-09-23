using Microsoft.AspNetCore.Identity;

namespace SafeSpend.Web.Services.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
