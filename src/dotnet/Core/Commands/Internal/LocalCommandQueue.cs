namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueue : ICommandQueue, ICommandQueueBackend
{
    private readonly Channel<QueuedCommand> _queue;
    private volatile int _successCount;
    private volatile int _failureCount;
    private volatile int _retryCount;

    public Symbol Name { get; }
    ICommandQueues ICommandQueue.Queues => Queues;
    public LocalCommandQueues Queues { get; }
    public int SuccessCount => _successCount;
    public int FailureCount => _failureCount;
    public int RetryCount => _retryCount;

    private IMomentClock Clock { get; }

    public LocalCommandQueue(Symbol name, LocalCommandQueues queues)
    {
        Name = name;
        Queues = queues;
        Clock = queues.Clock;
        _queue = Channel.CreateBounded<QueuedCommand>(
            new BoundedChannelOptions(Queues.Settings.MaxQueueSize) {
                FullMode = BoundedChannelFullMode.Wait,
            });
    }

    public Task Enqueue(QueuedCommand command, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(command, cancellationToken).AsTask();

    public IAsyncEnumerable<QueuedCommand> Read(CancellationToken cancellationToken)
        => _queue.Reader.ReadAllAsync(cancellationToken);

    public ValueTask MarkCompleted(QueuedCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _successCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailed(QueuedCommand command, bool mustRetry, Exception? exception, CancellationToken cancellationToken)
    {
        if (!mustRetry) {
            Interlocked.Increment(ref _failureCount);
            return ValueTask.CompletedTask;
        }

        Interlocked.Increment(ref _retryCount);
        var id = command.Id.Value;
        if (id.OrdinalIndexOf(" @retry-") is var retrySuffixStart and >= 0)
            id = id[..retrySuffixStart];
        var newTryIndex = command.TryIndex + 1;
        var newCommand = command with {
            Id = $"{id} @retry-{newTryIndex.Format()}",
            TryIndex = newTryIndex,
        };
        return _queue.Writer.WriteAsync(newCommand, cancellationToken);
    }
}
