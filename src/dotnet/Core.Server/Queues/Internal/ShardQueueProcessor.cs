using ActualChat.Concurrency;
using ActualLab.Diagnostics;

namespace ActualChat.Queues.Internal;

public abstract class ShardQueueProcessor<TSettings, TQueues, TMessage> : ShardWorker, IQueueProcessor
    where TSettings : QueueSettings
    where TQueues : IQueues
{
    private static bool DebugMode => Constants.DebugMode.QueueProcessor;

    private long _lastCommandCompletedAt;

    protected ICommander Commander { get; }
    protected CommandHandlerResolver CommandHandlerResolver { get; }
    protected new IMomentClock Clock { get; }
    protected new ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public TSettings Settings { get; }
    IQueues IQueueSender.Queues => Queues;
    public TQueues Queues { get; }
    public QueueRef QueueRef { get; }

    protected ShardQueueProcessor(TSettings settings, TQueues queues, QueueRef queueRef)
        : base(queues.Services, queueRef.ShardScheme, $"Queues.{queueRef.Format()}")
    {
        Settings = settings;
        Queues = queues;
        QueueRef = queueRef;

        Commander = Services.Commander();
        CommandHandlerResolver = Services.GetRequiredService<CommandHandlerResolver>();
        Clock = queues.Clock;
    }

    public abstract Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);
    public abstract Task Purge(CancellationToken cancellationToken);

    public virtual async Task WhenProcessing(TimeSpan maxCommandGap, CancellationToken cancellationToken = default)
    {
        var delay = maxCommandGap;
        while (delay > TimeSpan.Zero) {
            await Clock.Delay(delay + TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            var lastCommandCompletedAt = new Moment(Interlocked.Read(ref _lastCommandCompletedAt));
            delay = maxCommandGap - (Clock.Now - lastCommandCompletedAt);
        }
    }

    // Protected & private methods

    protected abstract Task MarkCompleted(
        int shardIndex, TMessage message, QueuedCommand? command, CancellationToken cancellationToken);
    protected abstract Task MarkFailed(
        int shardIndex, TMessage message, QueuedCommand? command, Exception exception, CancellationToken cancellationToken);
    protected abstract Task MarkPostponed(
        int shardIndex, TMessage message, QueuedCommand queuedCommand, TimeSpan delay, CancellationToken cancellationToken);

    protected virtual async ValueTask Process(int shardIndex, TMessage message, CancellationToken cancellationToken)
    {
        QueuedCommand queuedCommand;
        try {
            queuedCommand = Deserialize(message);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "[{ShardIndex}]: Couldn't deserialize the message", shardIndex);
            await MarkFailed(shardIndex, message, null, e, cancellationToken).ConfigureAwait(false);
            return;
        }

        var command = queuedCommand.UntypedCommand;
        var kind = command.GetKind();
        DebugLog?.LogDebug("[{ShardIndex}]: Running queued {Kind}: {Command}", shardIndex, kind, queuedCommand);
        try {
            var processTask = kind switch {
                CommandKind.Command => ProcessCommandOrBoundEvent(command, cancellationToken),
                CommandKind.BoundEvent => ProcessCommandOrBoundEvent(command, cancellationToken),
                CommandKind.UnboundEvent => ProcessUnboundEvent((IEventCommand)command, cancellationToken),
                _ => throw StandardError.Internal($"Invalid command kind: {kind}"),
            };
            await processTask.ConfigureAwait(false);
            await MarkCompleted(shardIndex, message, queuedCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e.GetBaseException() is PostponeException pe) {
                DebugLog?.LogDebug(e, "Queued {Kind} postponed: {Command}", kind, queuedCommand);
                await MarkPostponed(shardIndex, message, queuedCommand, pe.Delay, cancellationToken).ConfigureAwait(false);
                return;
            }
            Log.LogError(e, "[{ShardIndex}]: Queued {Kind} failed: {Command}", shardIndex, kind, queuedCommand);
            await MarkFailed(shardIndex, message, queuedCommand, e, cancellationToken).ConfigureAwait(false);
            if (e.IsCancellationOf(cancellationToken))
                throw;
        }
        finally {
            InterlockedExt.ExchangeIfGreater(ref _lastCommandCompletedAt, Clock.Now.EpochOffsetTicks);
        }
    }

    protected virtual Task ProcessCommandOrBoundEvent(
        ICommand command,
        CancellationToken cancellationToken)
        => Commander.Call(command, true, cancellationToken);

    protected virtual Task ProcessUnboundEvent(
        IEventCommand command,
        CancellationToken cancellationToken)
    {
        var handlers = CommandHandlerResolver.GetCommandHandlers(command.GetType());
        var handlerChains = handlers.HandlerChains;
        var enqueueTasks = new Task[handlerChains.Count];
        var index = 0;
        foreach (var (chainId, _) in handlerChains) {
            var enqueueTask = Queues.Enqueue(command.WithChainId(chainId), cancellationToken);
            enqueueTasks[index++] = enqueueTask;
        }
        return Task.WhenAll(enqueueTasks);
    }

    protected abstract QueuedCommand Deserialize(TMessage message);
}
