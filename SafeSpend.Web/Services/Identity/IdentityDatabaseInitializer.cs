using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Data;

namespace SafeSpend.Web.Services.Identity;

public static class IdentityDatabaseInitializer
{
    public const string InitialMigrationId =
        "20260923205308_InitialIdentitySchema";

    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyCollection<string>> RequiredLegacySchema =
        new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["AspNetRoles"] =
                ["Id", "Name", "NormalizedName", "ConcurrencyStamp"],
            ["AspNetRoleClaims"] =
                ["Id", "RoleId", "ClaimType", "ClaimValue"],
            ["AspNetUsers"] =
            [
                "Id", "DisplayName", "UserName", "NormalizedUserName",
                "Email", "NormalizedEmail", "EmailConfirmed",
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
                "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
                "LockoutEnd", "LockoutEnabled", "AccessFailedCount"
            ],
            ["AspNetUserClaims"] =
                ["Id", "UserId", "ClaimType", "ClaimValue"],
            ["AspNetUserLogins"] =
            [
                "LoginProvider", "ProviderKey", "ProviderDisplayName",
                "UserId"
            ],
            ["AspNetUserRoles"] = ["UserId", "RoleId"],
            ["AspNetUserTokens"] =
                ["UserId", "LoginProvider", "Name", "Value"]
        };

    public static async Task InitializeAsync(
        SafeSpendIdentityDbContext context,
        CancellationToken cancellationToken = default)
    {
        await LegacyDatabaseMigrationAdopter.MigrateAsync(
            context,
            InitialMigrationId,
            RequiredLegacySchema,
            cancellationToken);
    }
}
