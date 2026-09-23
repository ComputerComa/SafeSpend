using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Forecasting;

namespace SafeSpend.Web.Services.Plaid;

public sealed class SafeSpendDbContext(
    DbContextOptions<SafeSpendDbContext> options) : DbContext(options)
{
    public DbSet<PlaidItemEntity> PlaidItems => Set<PlaidItemEntity>();

    public DbSet<PlaidConnectionEntity> PlaidConnections =>
        Set<PlaidConnectionEntity>();

    public DbSet<PlaidTransactionEntity> PlaidTransactions =>
        Set<PlaidTransactionEntity>();

    public DbSet<PaycheckScheduleEntity> PaycheckSchedules =>
        Set<PaycheckScheduleEntity>();

    public DbSet<BillScheduleEntity> BillSchedules =>
        Set<BillScheduleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlaidItemEntity>(entity =>
        {
            entity.HasKey(row => row.ItemId);
            entity.Property(row => row.ItemId)
                .HasMaxLength(128);
            entity.Property(row => row.TransactionCursor)
                .HasMaxLength(512);
        });

        modelBuilder.Entity<PlaidConnectionEntity>(entity =>
        {
            entity.HasKey(row => row.UserId);
            entity.Property(row => row.UserId)
                .HasMaxLength(128);
            entity.Property(row => row.ItemId)
                .HasMaxLength(128)
                .IsRequired();
            entity.Property(row => row.ProtectedAccessToken)
                .HasMaxLength(4096)
                .IsRequired();
            entity.Property(row => row.TransactionCursor)
                .HasMaxLength(512);
            entity.Property(row => row.Status)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(row => row.LastWebhookCode)
                .HasMaxLength(128);
            entity.HasIndex(row => row.ItemId)
                .IsUnique();
        });

        modelBuilder.Entity<PlaidTransactionEntity>(entity =>
        {
            entity.HasKey(row => new
            {
                row.ItemId,
                row.TransactionId
            });

            entity.Property(row => row.ItemId)
                .HasMaxLength(128);
            entity.Property(row => row.TransactionId)
                .HasMaxLength(256);
            entity.Property(row => row.AccountId)
                .HasMaxLength(128);
            entity.Property(row => row.Amount)
                .HasPrecision(18, 2);
            entity.Property(row => row.Name)
                .HasMaxLength(512);
            entity.Property(row => row.MerchantName)
                .HasMaxLength(512);
            entity.Property(row => row.CurrencyCode)
                .HasMaxLength(32);
            entity.Property(row => row.PersonalFinancePrimaryCategory)
                .HasMaxLength(128);
            entity.Property(row => row.PersonalFinanceDetailedCategory)
                .HasMaxLength(128);
        });

        modelBuilder.Entity<PaycheckScheduleEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.AmountCents)
                .IsRequired();
            entity.Property(row => row.CushionCents)
                .IsRequired();
        });

        modelBuilder.Entity<BillScheduleEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Name)
                .HasMaxLength(256)
                .IsRequired();
            entity.Property(row => row.AmountCents)
                .IsRequired();
        });
    }
}

public sealed class PlaidItemEntity
{
    public required string ItemId { get; set; }

    public string? TransactionCursor { get; set; }
}

public sealed class PlaidConnectionEntity
{
    public required string UserId { get; set; }

    public required string ItemId { get; set; }

    public required string ProtectedAccessToken { get; set; }

    public string? TransactionCursor { get; set; }

    public required string Status { get; set; }

    public string? LastWebhookCode { get; set; }

    public DateTimeOffset? LastWebhookAt { get; set; }
}

public sealed class PlaidTransactionEntity
{
    public required string ItemId { get; set; }

    public required string TransactionId { get; set; }

    public required string AccountId { get; set; }

    public DateOnly? Date { get; set; }

    public decimal? Amount { get; set; }

    public required string Name { get; set; }

    public string? MerchantName { get; set; }

    public string? CurrencyCode { get; set; }

    public bool IsPending { get; set; }

    public string? PersonalFinancePrimaryCategory { get; set; }

    public string? PersonalFinanceDetailedCategory { get; set; }
}

public sealed class PaycheckScheduleEntity
{
    public int Id { get; set; }

    public DateOnly NextPaycheckDate { get; set; }

    public DateOnly FollowingPaycheckDate { get; set; }

    public long AmountCents { get; set; }

    public long CushionCents { get; set; }
}

public sealed class BillScheduleEntity
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public DateOnly NextDueDate { get; set; }

    public long AmountCents { get; set; }

    public int Frequency { get; set; }
}
