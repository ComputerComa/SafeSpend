using Microsoft.Extensions.Hosting;

namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidTransactionSyncWorker(
    IServiceScopeFactory scopeFactory,
    IPlaidSyncQueue syncQueue,
    IConfiguration configuration,
    ILogger<PlaidTransactionSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await SyncAllConnectionsAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(
            Math.Max(
                1,
                configuration.GetValue<int?>(
                    "SafeSpend:PlaidSyncIntervalMinutes") ?? 60));
        using var timer = new PeriodicTimer(interval);
        var queuedUser = syncQueue.DequeueAsync(stoppingToken).AsTask();
        var timerTick = timer.WaitForNextTickAsync(stoppingToken).AsTask();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(queuedUser, timerTick);

                if (completed == queuedUser)
                {
                    var userId = await queuedUser;
                    await SyncConnectionAsync(userId, stoppingToken);
                    queuedUser = syncQueue.DequeueAsync(stoppingToken)
                        .AsTask();
                }
                else
                {
                    _ = await timerTick;
                    await SyncAllConnectionsAsync(stoppingToken);
                    timerTick = timer.WaitForNextTickAsync(stoppingToken)
                        .AsTask();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task SyncAllConnectionsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var connectionStore = scope.ServiceProvider
                .GetRequiredService<IPlaidConnectionStore>();
            var connections = await connectionStore.GetAllAsync();

            foreach (var connection in connections)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await SyncConnectionAsync(
                    connection.UserId,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            logger.LogError(
                "Unable to enumerate Plaid connections for background sync. Error type: {ErrorType}",
                exception.GetType().Name);
        }
    }

    private async Task SyncConnectionAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var linkService = scope.ServiceProvider
                .GetRequiredService<PlaidLinkService>();
            var result = await linkService.SyncTransactionsForUserAsync(
                userId);

            logger.LogInformation(
                "Plaid background sync completed. Added: {AddedCount}; modified: {ModifiedCount}; removed: {RemovedCount}",
                result.Added.Count,
                result.Modified.Count,
                result.Removed.Count);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            logger.LogError(
                "Plaid background sync failed. Error type: {ErrorType}",
                exception.GetType().Name);
        }
    }
}
