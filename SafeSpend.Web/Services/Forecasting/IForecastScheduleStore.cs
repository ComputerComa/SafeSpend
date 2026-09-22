namespace SafeSpend.Web.Services.Forecasting;

public interface IForecastScheduleStore
{
    Task<PaycheckSchedule?> GetPaycheckScheduleAsync();

    Task<IReadOnlyList<BillSchedule>> GetBillSchedulesAsync();

    Task SavePaycheckScheduleAsync(PaycheckSchedule schedule);

    Task<int> AddBillScheduleAsync(BillSchedule schedule);

    Task DeleteBillScheduleAsync(int id);
}
