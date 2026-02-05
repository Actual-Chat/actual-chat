namespace ActualChat;

/// <summary>
/// An observable that supports multiple subscribers, each receiving published values
/// through their own channel.
/// </summary>
public interface IAsyncObservable<T>
{
    bool IsCompleted { get; }
    IAsyncSubscription<T> Subscribe(bool singleReader = true);
    void Publish(T value);
    bool TryComplete(Exception? error = null);
}

/// <summary>
/// A simple observable that supports multiple subscribers, each receiving published values
/// through their own channel.
/// </summary>
public sealed class AsyncObservable<T> : IAsyncObservable<T>
{
    private readonly ConcurrentDictionary<int, Channel<T>> _subscribers = new();
    private readonly Lock _lock = new();
    private volatile bool _isCompleted;
    private Exception? _completionError;
    private int _subscriberIdCounter;

    public bool IsCompleted => _isCompleted;

    public IAsyncSubscription<T> Subscribe(bool singleReader = true)
    {
        lock (_lock) {
            if (_isCompleted) {
                // Return a completed subscription
                var completedChannel = Channel.CreateUnbounded<T>();
                completedChannel.Writer.TryComplete(_completionError);
                return new Subscription(this, -1, completedChannel);
            }

            var subscriberId = Interlocked.Increment(ref _subscriberIdCounter);
            var options = singleReader
                ? ChannelExt.UnboundedPipeOptions
                : ChannelExt.UnboundedFanOutOptions;
            var channel = Channel.CreateUnbounded<T>(options);
            _subscribers.TryAdd(subscriberId, channel);
            return new Subscription(this, subscriberId, channel);
        }
    }

    public void Publish(T value)
    {
        if (_isCompleted)
            return;

        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(value);
    }

    public bool TryComplete(Exception? error = null)
    {
        lock (_lock) {
            if (_isCompleted)
                return false;

            _completionError = error;
            _isCompleted = true;

            foreach (var (_, channel) in _subscribers)
                channel.Writer.TryComplete(error);
            return true;
        }
    }

    private void Unsubscribe(int subscriberId)
    {
        if (subscriberId < 0)
            return; // Already completed subscription

        if (_subscribers.TryRemove(subscriberId, out var channel))
            channel.Writer.TryComplete();
    }

    // Nested types

    private sealed class Subscription(AsyncObservable<T> owner, int subscriberId, Channel<T> channel)
        : IAsyncSubscription<T>
    {
        private volatile int _isDisposed;

        public ChannelReader<T> Reader => channel.Reader;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return default;

            owner.Unsubscribe(subscriberId);
            return default;
        }
    }
}
