namespace ActualChat.Commands.Internal;

public sealed class LocalCommandQueue : ICommandQueue, ICommandQueueBackend
{
    private readonly Channel<QueuedCommand> _queue;
    private volatile int _successCount;
    private volatile int _failureCount;

    public QueueId QueueId { get; }
    public LocalCommandQueues Queues { get; }
    public int SuccessCount => _successCount;
    public int FailureCount => _failureCount;

    private IMomentClock Clock { get; }

    public LocalCommandQueue(QueueId queueId, LocalCommandQueues queues)
    {
        QueueId = queueId;
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
        return default;
    }

    public ValueTask MarkFailed(QueuedCommand command, Exception? exception, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _failureCount);
        return default;
    }

    public ValueTask Purge(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
