using System.Collections.Concurrent;

namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidSyncCoordinator
{
    Task<T> RunAsync<T>(string userId, Func<Task<T>> operation);
}

public sealed class PlaidSyncCoordinator : IPlaidSyncCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

    public async Task<T> RunAsync<T>(
        string userId,
        Func<Task<T>> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(operation);

        var semaphore = _locks.GetOrAdd(
            userId,
            static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            return await operation();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
