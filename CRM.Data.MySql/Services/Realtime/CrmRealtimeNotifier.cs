using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CRM.Data.Services;

public sealed class CrmRealtimeNotifier
{
    private readonly ConcurrentDictionary<Guid, Channel<long>> _subscribers = new();
    private long _version;

    public CrmRealtimeSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _subscribers[id] = channel;
        return new CrmRealtimeSubscription(id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void PublishChange()
    {
        var version = Interlocked.Increment(ref _version);
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(version);
        }
    }
}

public sealed record CrmRealtimeSubscription(Guid Id, ChannelReader<long> Reader);
