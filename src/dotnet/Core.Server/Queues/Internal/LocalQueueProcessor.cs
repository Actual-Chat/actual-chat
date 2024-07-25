using ActualChat.Concurrency;
using ActualChat.Module;
using ActualLab.Diagnostics;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ActualChat.Queues.Internal;

public abstract class LocalQueueProcessor<TSettings, TQueues> : WorkerBase, IQueueProcessor
    where TQueues : IQueues
{
    private static ActivitySource QueueActivitySource {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => CoreServerModuleInstrumentation.ActivitySource;
    }

    private readonly string ProcessActivityName;

    private static bool DebugMode => Constants.DebugMode.QueueProcessor;

    private long _lastCommandCompletedAt;

    protected IServiceProvider Services { get; }
    protected ICommander Commander { get; }
    protected IMomentClock Clock { get; }
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;
    protected ILogger Log { get; }

    public TSettings Settings { get; }
    IQueues IQueueSender.Queues => Queues;
    public TQueues Queues { get; }

    protected LocalQueueProcessor(TSettings settings, TQueues queues)
    {
        Settings = settings;
        Queues = queues;

        Services = queues.Services;
        Commander = Services.Commander();
        Clock = queues.Clock;
        Log = Services.LogFor(GetType());

        ProcessActivityName = $"{nameof(Process)}@{GetType().Name}";
    }

    public abstract Task Enqueue(QueueShardRef queueShardRef, QueuedCommand queuedCommand, CancellationToken cancellationToken = default);

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

    protected abstract bool MarkKnown(QueuedCommand command);
    protected abstract void MarkUnknown(QueuedCommand command);

    protected virtual async ValueTask Process(QueuedCommand queuedCommand, CancellationToken cancellationToken)
    {
        if (!MarkKnown(queuedCommand))
            return;

        ActivityContext senderContext = default;
        IEnumerable<ActivityLink>? links = null;
        var propagationContext = Propagators.DefaultTextMapPropagator
            .Extract(default, queuedCommand.Headers, static (headers, name) => headers.TryGetValue(name, out var value) ? value : []);
        if (propagationContext != default) {
            senderContext = propagationContext.ActivityContext;
            Baggage.Current = propagationContext.Baggage;
            links = [new ActivityLink(senderContext)];
        }

        using var activity = QueueActivitySource
            .StartActivity(ProcessActivityName, ActivityKind.Consumer, senderContext, links: links);

        var command = queuedCommand.UntypedCommand;
        var kind = command.GetKind();
        DebugLog?.LogDebug("Running queued {Kind}: {Command}", kind, queuedCommand);
        try {
            // This call takes care of reprocessing
            await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            await MarkCompleted(queuedCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e.GetBaseException() is PostponeException pe) {
                DebugLog?.LogDebug(e, "Queued {Kind} postponed: {Command}", kind, queuedCommand);
                await MarkPostponed(queuedCommand, pe.Delay, cancellationToken).ConfigureAwait(false);
                return;
            }
            Log.LogError(e, "Queued {Kind} failed: {Command}", kind, queuedCommand);
            await MarkFailed(queuedCommand, e, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        finally {
            InterlockedExt.ExchangeIfGreater(ref _lastCommandCompletedAt, Clock.Now.EpochOffsetTicks);
        }
    }

    protected virtual Task MarkCompleted(QueuedCommand queuedCommand, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task MarkFailed(QueuedCommand queuedCommand, Exception exception, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task MarkPostponed(QueuedCommand queuedCommand, TimeSpan delay, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () => {
            await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            MarkUnknown(queuedCommand);
            await Queues.Enqueue(queuedCommand, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
        return Task.CompletedTask;
    }
}
