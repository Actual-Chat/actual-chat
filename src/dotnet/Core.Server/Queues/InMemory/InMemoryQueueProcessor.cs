using ActualChat.Queues.Internal;

namespace ActualChat.Queues.InMemory;

public sealed class InMemoryQueueProcessor : LocalQueueProcessor<InMemoryQueues.Options, InMemoryQueues>
{
    private readonly Channel<QueuedCommand> _queue;
    private readonly RecentlySeenMap<string, Unit> _knownCommands;

    public InMemoryQueueProcessor(InMemoryQueues.Options settings, InMemoryQueues queues)
        : base(settings, queues)
    {
        _queue = Channel.CreateBounded<QueuedCommand>(
            new BoundedChannelOptions(Settings.MaxQueueSize) {
                FullMode = BoundedChannelFullMode.Wait,
            });
        _knownCommands = new RecentlySeenMap<string, Unit>(
            Settings.MaxKnownCommandCount,
            Settings.MaxKnownCommandAge,
            keyComparer: StringComparer.Ordinal);
    }

    public override Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(queuedCommand, cancellationToken).AsTask();

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        using var gracefulStopCts = cancellationToken.CreateDelayedTokenSource(Settings.GracefulStopDelay);
        var gracefulStopToken = gracefulStopCts.Token;

        // ReSharper disable once PossiblyMistakenUseOfCancellationToken
        await _queue.ProcessConcurrently(Settings.ConcurrencyLevel, ProcessImpl, cancellationToken).ConfigureAwait(false);
        return;

        ValueTask ProcessImpl(QueuedCommand queuedCommand, CancellationToken _)
            => Process(queuedCommand, gracefulStopToken);
    }

    protected override bool MarkKnown(QueuedCommand command)
    {
        lock (_knownCommands) {
            // We de-duplicate commands here to make sure we process them just once.
            // Note that here we rely on QueuedCommand.Uuid instead of its Command value,
            // coz Command instance is definitely non-unique here due to deserialization.
            return _knownCommands.TryAdd(command.Uuid);
        }
    }

    protected override void MarkUnknown(QueuedCommand command)
    {
        lock (_knownCommands)
            _knownCommands.TryRemove(command.Uuid);
    }
}
