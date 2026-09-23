using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Data;

namespace SafeSpend.Web.Services.Plaid;

public static class SafeSpendDatabaseInitializer
{
    public const string InitialMigrationId =
        "20260923205302_InitialSafeSpendSchema";

    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyCollection<string>> RequiredLegacySchema =
        new Dictionary<string, IReadOnlyCollection<string>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["BillSchedules"] =
            [
                "Id", "Name", "NextDueDate", "AmountCents", "Frequency"
            ],
            ["PaycheckSchedules"] =
            [
                "Id", "NextPaycheckDate", "FollowingPaycheckDate",
                "AmountCents", "CushionCents"
            ],
            ["PlaidConnections"] =
            [
                "UserId", "ItemId", "ProtectedAccessToken",
                "TransactionCursor", "Status", "LastWebhookCode",
                "LastWebhookAt"
            ],
            ["PlaidItems"] = ["ItemId", "TransactionCursor"],
            ["PlaidTransactions"] =
            [
                "ItemId", "TransactionId", "AccountId", "Date", "Amount",
                "Name", "MerchantName", "CurrencyCode", "IsPending",
                "PersonalFinancePrimaryCategory",
                "PersonalFinanceDetailedCategory"
            ]
        };

    public static async Task InitializeAsync(
        SafeSpendDbContext context,
        CancellationToken cancellationToken = default)
    {
        await LegacyDatabaseMigrationAdopter.MigrateAsync(
            context,
            InitialMigrationId,
            RequiredLegacySchema,
            cancellationToken);
    }
}
