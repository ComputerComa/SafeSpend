namespace SafeSpend.Web.Services.Forecasting;

public sealed class CashFlowScheduleService
{
    public IReadOnlyList<CashFlowOccurrence> BuildOccurrences(
        IEnumerable<BillSchedule> schedules,
        DateOnly fromDate,
        DateOnly throughDate)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        if (throughDate <= fromDate)
        {
            throw new ArgumentException(
                "The schedule window must end after it starts.");
        }

        return schedules
            .SelectMany(schedule => ExpandSchedule(
                schedule,
                fromDate,
                throughDate))
            .OrderBy(occurrence => occurrence.Date)
            .ThenBy(occurrence => occurrence.Name)
            .ToArray();
    }

    private static IEnumerable<CashFlowOccurrence> ExpandSchedule(
        BillSchedule schedule,
        DateOnly fromDate,
        DateOnly throughDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule.Name);

        if (schedule.AmountCents <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedule.AmountCents));
        }

        var date = schedule.NextDueDate;

        while (date < fromDate && schedule.Frequency != BillFrequency.OneTime)
        {
            date = Advance(date, schedule.Frequency);
        }

        while (date < throughDate)
        {
            if (date >= fromDate)
            {
                yield return new CashFlowOccurrence(
                    schedule.Name,
                    date,
                    schedule.AmountCents,
                    CashFlowType.Expense);
            }

            if (schedule.Frequency == BillFrequency.OneTime)
            {
                yield break;
            }

            date = Advance(date, schedule.Frequency);
        }
    }

    private static DateOnly Advance(
        DateOnly date,
        BillFrequency frequency)
    {
        return frequency switch
        {
            BillFrequency.Weekly => date.AddDays(7),
            BillFrequency.Biweekly => date.AddDays(14),
            BillFrequency.Monthly => date.AddMonths(1),
            _ => date
        };
    }
}
