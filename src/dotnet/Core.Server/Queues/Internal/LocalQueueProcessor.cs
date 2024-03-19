using ActualChat.Concurrency;
using ActualLab.Diagnostics;

namespace ActualChat.Queues.Internal;

public abstract class LocalQueueProcessor<TSettings, TQueues> : WorkerBase, IQueueProcessor
    where TQueues : IQueues
{
    private static bool DebugMode => Constants.DebugMode.CommandQueue;

    private long _lastCommandCompletedAt;

    protected IServiceProvider Services { get; }
    protected ICommander Commander { get; }
    protected IMomentClock Clock { get; }
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;
    protected ILogger Log { get; }

    public TSettings Settings { get; }
    IQueues IQueueProcessor.Queues => Queues;
    public TQueues Queues { get; }

    protected LocalQueueProcessor(TSettings settings, TQueues queues)
    {
        Settings = settings;
        Queues = queues;

        Services = queues.Services;
        Commander = Services.Commander();
        Clock = queues.Clock;
        Log = Services.LogFor(GetType());
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
