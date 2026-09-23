using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SafeSpend.Web.Data.Migrations.SafeSpend
{
    /// <inheritdoc />
    public partial class InitialSafeSpendSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaycheckSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NextPaycheckDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FollowingPaycheckDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountCents = table.Column<long>(type: "INTEGER", nullable: false),
                    CushionCents = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaycheckSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaidConnections",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    TransactionCursor = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastWebhookCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastWebhookAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidConnections", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "PlaidItems",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TransactionCursor = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidItems", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "PlaidTransactions",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TransactionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    MerchantName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsPending = table.Column<bool>(type: "INTEGER", nullable: false),
                    PersonalFinancePrimaryCategory = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PersonalFinanceDetailedCategory = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaidTransactions", x => new { x.ItemId, x.TransactionId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaidConnections_ItemId",
                table: "PlaidConnections",
                column: "ItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillSchedules");

            migrationBuilder.DropTable(
                name: "PaycheckSchedules");

            migrationBuilder.DropTable(
                name: "PlaidConnections");

            migrationBuilder.DropTable(
                name: "PlaidItems");

            migrationBuilder.DropTable(
                name: "PlaidTransactions");
        }
    }
}
