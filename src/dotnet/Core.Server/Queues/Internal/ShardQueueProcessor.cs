using ActualChat.Concurrency;
using ActualChat.Diagnostics;
using ActualChat.Time;
using ActualLab.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Queues.Internal;

public abstract class ShardQueueProcessor<TSettings, TQueues, TMessage> : ShardWorker, IQueueProcessor
    where TSettings : QueueSettings
    where TQueues : IQueues
{
    private static bool DebugMode => Constants.DebugMode.QueueProcessors;

    private readonly AsyncTaskMethodBuilder _whenStarted = AsyncTaskMethodBuilderExt.New();
    private long _lastCommandCompletedAt;

    protected ICommander Commander { get; }
    protected CommandHandlerResolver CommandHandlerResolver { get; }
    protected MomentClock Clock { get; }
    protected new ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public TSettings Settings { get; }
    public TQueues Queues { get; }
    public QueueRef QueueRef { get; }

    protected ShardQueueProcessor(TSettings settings, TQueues queues, QueueRef queueRef)
        : base(queues.Services, queueRef.ShardScheme)
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
        DebugLog?.LogDebug("{QueueRef}.WhenProcessing: waiting for start", QueueRef);
        await _whenStarted.Task.ConfigureAwait(false);
        var delay = maxCommandGap;
        while (delay > TimeSpan.Zero) {
            DebugLog?.LogDebug("{QueueRef}.WhenProcessing: waiting for {Delay}", QueueRef, delay.ToShortString());
            await Clock.Delay(delay + TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            var lastCommandCompletedAt = new Moment(Interlocked.Read(ref _lastCommandCompletedAt));
            delay = lastCommandCompletedAt + maxCommandGap - Clock.Now;
        }
        DebugLog?.LogDebug("{QueueRef}.WhenProcessing: done", QueueRef);
    }

    // Protected & private methods

    protected abstract Task MarkCompleted(
        int shardIndex, TMessage message, QueuedCommand? command, CancellationToken cancellationToken);
    protected abstract Task MarkFailed(
        int shardIndex, TMessage message, QueuedCommand? command, Exception exception, CancellationToken cancellationToken);
    protected abstract Task MarkPostponed(
        int shardIndex, TMessage message, QueuedCommand command, TimeSpan delay, CancellationToken cancellationToken);

    protected void MarkStarted()
    {
        if (_whenStarted.TrySetResult())
            InterlockedExt.ExchangeIfGreater(ref _lastCommandCompletedAt, Clock.Now.EpochOffsetTicks);
    }

    protected async ValueTask Process(
        int shardIndex,
        TMessage message,
        QueuedCommand queuedCommand,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        var command = queuedCommand.UntypedCommand;
        var kind = command.GetKind();
        var timeout = QueuesExt.GetTimeout(queuedCommand.UntypedCommand, Services);
        DebugLog?.LogDebug(
            "[{ShardScheme}-S{ShardIndex}] Running queued {Kind} #{Uuid}: {Command}",
            ShardScheme.Name, shardIndex, kind, queuedCommand.Uuid, queuedCommand.UntypedCommand);

        using var timeoutCts = cancellationToken.CreateLinkedTokenSource();
        var timeoutToken = timeoutCts.Token;
        timeoutCts.CancelAfter(timeout);

        ActivityContext senderContext = default;
        IEnumerable<ActivityLink>? links = null;
        var propagationContext = Propagators.DefaultTextMapPropagator
            .Extract(default, queuedCommand.Headers, static (headers, name) => headers.TryGetValue(name, out var value) ? value : []);

        if (propagationContext != default) {
            senderContext = propagationContext.ActivityContext;
            Baggage.Current = propagationContext.Baggage;
            links = [new ActivityLink(senderContext)];
        }

        var operationName = GetType().GetOperationName();
        using var activity = CoreServerInstruments.ActivitySource
            .StartActivity(operationName, ActivityKind.Consumer, senderContext, links: links);

        try {
            if (command is IHasDelayUntil du && du.DelayUntil - Clock.Now is var delay && delay > TimeSpan.Zero) {
                activity?.SetStatus(ActivityStatusCode.Ok, $"Postponed for {delay.ToShortString()}");
                activity?.AddTag(OtelConstants.ProcessingStatusTag, OtelConstants.ProcessingStatus.Postponed);
                // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                await MarkPostponed(shardIndex, message, queuedCommand, delay, cancellationToken).ConfigureAwait(false);
                DebugLog?.LogDebug(
                    "[{ShardScheme}-S{ShardIndex}] Queued {Kind} #{Uuid} postponed: {Command} after {Time}",
                    ShardScheme.Name, shardIndex, kind, queuedCommand.Uuid, queuedCommand.UntypedCommand, sw.Elapsed);
            }
            else {
                try {
                    var processTask = kind switch {
                        CommandKind.Command => ProcessCommandOrBoundEvent(command, timeoutToken),
                        CommandKind.BoundEvent => ProcessCommandOrBoundEvent(command, timeoutToken),
                        CommandKind.UnboundEvent => ProcessUnboundEvent((IEventCommand)command, timeoutToken),
                        _ => throw StandardError.Internal($"Invalid command kind: {kind}"),
                    };
                    await processTask.ConfigureAwait(false);
                }
                catch (Exception e) when (e.IsCancellationOfTimeoutToken(timeoutToken, cancellationToken)) {
                    throw StandardError.Timeout($"Queued {kind}");
                }
                DebugLog?.LogDebug(
                    "[{ShardScheme}-S{ShardIndex}] Queued {Kind} #{Uuid} completed: {Command} in {Time}",
                    ShardScheme.Name, shardIndex, kind, queuedCommand.Uuid, queuedCommand.UntypedCommand, sw.Elapsed);
                // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                await MarkCompleted(shardIndex, message, queuedCommand, cancellationToken).ConfigureAwait(false);
                activity?.AddTag(OtelConstants.ProcessingStatusTag, OtelConstants.ProcessingStatus.Completed);
            }
        }
        catch (Exception e) when (e.GetBaseException() is PostponeException pe) {
            activity?.SetStatus(ActivityStatusCode.Ok, e.Message);
            activity?.AddTag(OtelConstants.ProcessingStatusTag, OtelConstants.ProcessingStatus.Postponed);
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            await MarkPostponed(shardIndex, message, queuedCommand, pe.Delay, cancellationToken).ConfigureAwait(false);
            DebugLog?.LogDebug(e,
                "[{ShardScheme}-S{ShardIndex}] Queued {Kind} #{Uuid} postponed: {Command} after {Time}",
                ShardScheme.Name, shardIndex, kind, queuedCommand.Uuid, queuedCommand.UntypedCommand, sw.Elapsed);
        }
        catch (Exception e) {
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            if (e.IsCancellationOf(cancellationToken)) {
                activity?.SetStatus(ActivityStatusCode.Ok, $"{GetType().Name} is stopping, processing cancelled.");
                activity?.AddTag(OtelConstants.ProcessingStatusTag, OtelConstants.ProcessingStatus.Canceled);
                throw;
            }
            Log.LogError(e,
                "[{ShardScheme}-S{ShardIndex}] Queued {Kind} #{Uuid} failed: {Command}",
                ShardScheme.Name, shardIndex, kind, queuedCommand.Uuid, queuedCommand.UntypedCommand);

            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            activity?.AddTag(OtelConstants.ProcessingStatusTag, OtelConstants.ProcessingStatus.Failed);
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            await MarkFailed(shardIndex, message, queuedCommand, e, cancellationToken).ConfigureAwait(false);
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
}
