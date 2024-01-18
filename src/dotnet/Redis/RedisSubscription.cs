using ActualChat.Channels.Internal;
using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisSubscription<T> : IAsyncSubscription<T>
{
    private ChannelMessageQueue? _queue;
    private volatile Task? _unsubscribeTask;

    public ChannelReader<T> Reader { get; }

    public RedisSubscription(ChannelMessageQueue queue, Func<ChannelMessage, T> mapper)
    {
        _queue = queue;
        Reader = new MappingChannelReader<T, ChannelMessage>(_queue.GetQueueReader(), mapper);
    }

    public ValueTask DisposeAsync()
    {
        var queue = Interlocked.Exchange(ref _queue, null);
        if (queue == null)
            return (_unsubscribeTask ?? Task.CompletedTask).ToValueTask();

        var unsubscribeTask = queue.UnsubscribeAsync(CommandFlags.FireAndForget);
        _ = Interlocked.Exchange(ref _unsubscribeTask, unsubscribeTask);
        return unsubscribeTask.ToValueTask();
    }
}
