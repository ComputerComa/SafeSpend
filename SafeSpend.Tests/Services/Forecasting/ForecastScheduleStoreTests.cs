using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Forecasting;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Tests.Services.Forecasting;

public sealed class ForecastScheduleStoreTests
{
    [Fact]
    public async Task SchedulesPersistAcrossStoreInstances()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"safespend-schedules-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<SafeSpendDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var context = new SafeSpendDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            var store = new ForecastScheduleStore(
                new TestDbContextFactory(options));

            await store.SavePaycheckScheduleAsync(
                new PaycheckSchedule(
                    new DateOnly(2026, 9, 30),
                    new DateOnly(2026, 10, 14),
                    135_000,
                    10_000));
            await store.AddBillScheduleAsync(
                new BillSchedule(
                    0,
                    "Internet",
                    new DateOnly(2026, 10, 3),
                    13_300,
                    BillFrequency.Monthly));

            var reloadedStore = new ForecastScheduleStore(
                new TestDbContextFactory(options));
            var paycheck = await reloadedStore
                .GetPaycheckScheduleAsync();
            var bills = await reloadedStore.GetBillSchedulesAsync();

            Assert.NotNull(paycheck);
            Assert.Equal(135_000, paycheck.AmountCents);
            var bill = Assert.Single(bills);
            Assert.Equal("Internet", bill.Name);
            Assert.Equal(BillFrequency.Monthly, bill.Frequency);

            await reloadedStore.DeleteBillScheduleAsync(bill.Id);
            Assert.Empty(await reloadedStore.GetBillSchedulesAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<SafeSpendDbContext> options)
        : IDbContextFactory<SafeSpendDbContext>
    {
        public SafeSpendDbContext CreateDbContext() =>
            new(options);

        public Task<SafeSpendDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafeSpendDbContext(options));
    }
}
