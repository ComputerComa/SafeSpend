using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SafeSpend.Web.Services.Identity;

public sealed class SafeSpendIdentityDbContext(
    DbContextOptions<SafeSpendIdentityDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
}
