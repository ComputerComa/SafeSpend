using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SafeSpend.Web.Services.Plaid;

public interface IPlaidSyncQueue
{
    bool Enqueue(string userId);

    ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class PlaidSyncQueue : IPlaidSyncQueue
{
    private readonly Channel<string> _channel =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _pending = [];

    public bool Enqueue(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_pending.TryAdd(userId, 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(userId))
        {
            return true;
        }

        _pending.TryRemove(userId, out _);
        return false;
    }

    public async ValueTask<string> DequeueAsync(
        CancellationToken cancellationToken)
    {
        var userId = await _channel.Reader.ReadAsync(cancellationToken);
        _pending.TryRemove(userId, out _);
        return userId;
    }
}
