using Microsoft.EntityFrameworkCore;
using SafeSpend.Web.Services.Plaid;

namespace SafeSpend.Web.Services.Forecasting;

public sealed class ForecastScheduleStore(
    IDbContextFactory<SafeSpendDbContext> contextFactory)
    : IForecastScheduleStore
{
    private const int PaycheckScheduleId = 1;

    public async Task<PaycheckSchedule?> GetPaycheckScheduleAsync()
    {
        await using var context =
            await contextFactory.CreateDbContextAsync();

        var schedule = await context.PaycheckSchedules
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == PaycheckScheduleId);

        return schedule is null
            ? null
            : new PaycheckSchedule(
                schedule.NextPaycheckDate,
                schedule.FollowingPaycheckDate,
                schedule.AmountCents,
                schedule.CushionCents);
    }

    public async Task<IReadOnlyList<BillSchedule>>
        GetBillSchedulesAsync()
    {
        await using var context =
            await contextFactory.CreateDbContextAsync();

        var schedules = await context.BillSchedules
            .AsNoTracking()
            .OrderBy(row => row.NextDueDate)
            .ThenBy(row => row.Name)
            .ToListAsync();

        return schedules
            .Select(schedule => new BillSchedule(
                schedule.Id,
                schedule.Name,
                schedule.NextDueDate,
                schedule.AmountCents,
                (BillFrequency)schedule.Frequency))
            .ToArray();
    }

    public async Task SavePaycheckScheduleAsync(
        PaycheckSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        await using var context =
            await contextFactory.CreateDbContextAsync();

        var entity = await context.PaycheckSchedules
            .SingleOrDefaultAsync(row => row.Id == PaycheckScheduleId);

        if (entity is null)
        {
            entity = new PaycheckScheduleEntity
            {
                Id = PaycheckScheduleId
            };
            context.PaycheckSchedules.Add(entity);
        }

        entity.NextPaycheckDate = schedule.NextPaycheckDate;
        entity.FollowingPaycheckDate = schedule.FollowingPaycheckDate;
        entity.AmountCents = schedule.AmountCents;
        entity.CushionCents = schedule.CushionCents;

        await context.SaveChangesAsync();
    }

    public async Task<int> AddBillScheduleAsync(BillSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var entity = new BillScheduleEntity
        {
            Name = schedule.Name,
            NextDueDate = schedule.NextDueDate,
            AmountCents = schedule.AmountCents,
            Frequency = (int)schedule.Frequency
        };

        await using var context =
            await contextFactory.CreateDbContextAsync();
        context.BillSchedules.Add(entity);
        await context.SaveChangesAsync();

        return entity.Id;
    }

    public async Task DeleteBillScheduleAsync(int id)
    {
        await using var context =
            await contextFactory.CreateDbContextAsync();

        var entity = await context.BillSchedules
            .SingleOrDefaultAsync(row => row.Id == id);

        if (entity is null)
        {
            return;
        }

        context.BillSchedules.Remove(entity);
        await context.SaveChangesAsync();
    }
}
