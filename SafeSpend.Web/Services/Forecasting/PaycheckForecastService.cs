namespace SafeSpend.Web.Services.Forecasting;

public sealed class PaycheckForecastService
{
    public PaycheckForecast Calculate(PaycheckForecastInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Occurrences);

        if (input.FollowingPaycheckDate <= input.NextPaycheckDate)
        {
            throw new ArgumentException(
                "The following paycheck must occur after the next paycheck.");
        }

        if (input.CushionCents < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.CushionCents));
        }

        var activeOccurrences = input.Occurrences
            .Where(occurrence => !occurrence.IsSettled)
            .ToArray();

        var beforeNextPaycheck = activeOccurrences
            .Where(occurrence =>
                occurrence.Date >= input.AsOfDate &&
                occurrence.Date < input.NextPaycheckDate)
            .ToArray();

        var nextPayPeriod = activeOccurrences
            .Where(occurrence =>
                occurrence.Date >= input.NextPaycheckDate &&
                occurrence.Date < input.FollowingPaycheckDate)
            .ToArray();

        var projectedBalanceBeforeNextPaycheck =
            input.AvailableBalanceCents +
            beforeNextPaycheck.Sum(occurrence =>
                occurrence.SignedAmountCents);

        var safeToSpendNow =
            projectedBalanceBeforeNextPaycheck -
            input.CushionCents;

        var availableFromNextPaycheck =
            projectedBalanceBeforeNextPaycheck +
            input.NextPaycheckAmountCents +
            nextPayPeriod.Sum(occurrence =>
                occurrence.SignedAmountCents) -
            input.CushionCents;

        return new PaycheckForecast(
            SafeToSpendNowCents: safeToSpendNow,
            AvailableFromNextPaycheckCents: availableFromNextPaycheck,
            ProjectedBalanceBeforeNextPaycheckCents:
                projectedBalanceBeforeNextPaycheck,
            BillsBeforeNextPaycheckCents:
                SumExpenses(beforeNextPaycheck),
            BillsInNextPayPeriodCents:
                SumExpenses(nextPayPeriod));
    }

    private static long SumExpenses(
        IEnumerable<CashFlowOccurrence> occurrences)
    {
        return occurrences
            .Where(occurrence =>
                occurrence.Type == CashFlowType.Expense)
            .Sum(occurrence => occurrence.AmountCents);
    }
}
