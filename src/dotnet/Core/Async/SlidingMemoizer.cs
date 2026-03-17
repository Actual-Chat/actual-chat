namespace ActualChat;

/// <summary>
/// Memoizes an async sequence with a fixed-capacity ring buffer.
/// Only the most recent <paramref name="capacity"/> items are kept.
/// Multiple consumers can replay the current buffer and receive live updates.
/// </summary>
public sealed class SlidingMemoizer<T> : IAsyncMemoizer<T>
{
    private readonly T[] _ring;
    private readonly int _capacity;
    private readonly int _consumerCapacity;
    private readonly HashSet<ChannelWriter<T>> _targets = new();
    private readonly Lock _sync = new();
    private long _writePos;
    private bool _completed;
    private Exception? _completionError;

    public Task WriteTask { get; }

    public SlidingMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        CancellationToken cancellationToken = default)
        : this(source, capacity, capacity, cancellationToken)
    { }

    public SlidingMemoizer(
        IAsyncEnumerable<T> source,
        int capacity,
        int consumerCapacity,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consumerCapacity);

        _capacity = capacity;
        _consumerCapacity = consumerCapacity;
        _ring = new T[capacity];
        WriteTask = BackgroundTask.Run(() => ReadLoop(source, cancellationToken), cancellationToken);
    }

    public IAsyncEnumerable<T> Replay(CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_consumerCapacity) {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        lock (_sync) {
            var start = Math.Max(0, _writePos - Math.Min(_capacity, _consumerCapacity));
            for (var i = start; i < _writePos; i++)
                channel.Writer.TryWrite(_ring[i % _capacity]);

            if (!_completed)
                _targets.Add(channel.Writer);
            else if (_completionError != null)
                channel.Writer.TryComplete(_completionError);
            else
                channel.Writer.TryComplete();
        }
        return ReadChannel(channel, cancellationToken);
    }

    // Private methods

    private async IAsyncEnumerable<T> ReadChannel(
        Channel<T> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (reader.TryRead(out var item)) {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
        finally {
            lock (_sync)
                _targets.Remove(channel.Writer);
            channel.Writer.TryComplete();
        }
    }

    private async Task ReadLoop(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        var closedTargets = (List<ChannelWriter<T>>?)null;
        try {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                lock (_sync) {
                    _ring[_writePos % _capacity] = item;
                    _writePos++;
                    foreach (var target in _targets)
                        if (!target.TryWrite(item)) {
                            closedTargets ??= new List<ChannelWriter<T>>();
                            closedTargets.Add(target);
                        }
                    if (closedTargets is { Count: > 0 }) {
                        foreach (var closed in closedTargets) {
                            _targets.Remove(closed);
                            closed.TryComplete();
                        }
                        closedTargets.Clear();
                    }
                }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            lock (_sync) {
                _completed = true;
                _completionError = e;
                foreach (var target in _targets)
                    target.TryComplete(e);
                _targets.Clear();
            }
            throw;
        }

        lock (_sync) {
            _completed = true;
            foreach (var target in _targets)
                target.TryComplete();
            _targets.Clear();
        }
    }
}
