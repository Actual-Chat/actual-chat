using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

public abstract class IndexingFlowBase<TCursor> : Flow
{
    [DataMember(Order = 100), MemoryPackOrder(100)]
    public TCursor? Cursor { get; protected set; }

    [DataMember(Order = 102), MemoryPackOrder(102)]
    public Moment? NextWatchdogTimerAt { get; protected set; }

    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan WatchdogInterval { get; } = TimeSpan.FromHours(24);
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan TimerRescheduleThreshold { get; } = TimeSpan.FromSeconds(1);

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        if (!await OnBeforeFirstIndexAfterReset(cancellationToken).ConfigureAwait(false))
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);

        return Resume(nameof(OnIndex));
    }

    protected virtual Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
        => ActualLab.Async.TaskExt.TrueTask;

    protected abstract Task<BatchIndexingResult<TCursor>> Process(TCursor? cursor, CancellationToken cancellationToken);

    protected virtual async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var (mustEnd, isTailReached, updatedCursor) = await Process(Cursor, cancellationToken).ConfigureAwait(false);
        Cursor = updatedCursor;
        Log.LogInformation(
            "`{Id}`.OnIndex: processed portion: MustEnd={MustEnd}, IsTailReached={IsTailReached}, {@UpdatedCursor}",
            Id,
            mustEnd,
            isTailReached,
            updatedCursor);
        if (isTailReached && !await OnTailReached(cancellationToken).ConfigureAwait(false)) {
            Log.LogInformation("`{Id}`.OnIndex: forced to suspend flow after tail handling", Id);
            mustEnd = true;
        }
        if (mustEnd)
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt); // i.e. chat is removed

        return isTailReached
            ? WaitForWatchdog()
            : QueueResume(nameof(OnIndex), "Continue processing when possible");
    }

    protected virtual Task<bool> OnTailReached(CancellationToken cancellationToken)
    {
        Log.LogInformation("`{Id}`.OnTailReached: {Cursor}", Id, Cursor);
        return ActualLab.Async.TaskExt.TrueTask;
    }

    private FlowTransition WaitForWatchdog()
    {
        if (GetNextWatchdogAt() is { } nextWatchdogAt) {
            NextWatchdogTimerAt = nextWatchdogAt;
            Log.LogInformation("`{Id}`.GetTransition: Waiting for watchdog timer at {NextTimerAt}", Id, nextWatchdogAt);
            return WaitForTimer(nameof(OnIndex), nextWatchdogAt, "Waiting for watchdog timer");
        }

        Log.LogInformation("`{Id}`.GetTransition: Suspending flow", Id);
        return WaitForEvent(nameof(OnIndex), InfiniteHardResumeAt, "Watchdog is already set");
    }

    private Moment? GetNextWatchdogAt()
    {
        var now = Clocks.SystemClock.Now;
        if (NextWatchdogTimerAt == null)
            return now + WatchdogInterval;

        if (NextWatchdogTimerAt <= now + TimerRescheduleThreshold)
            return now + WatchdogInterval;

        return null;
    }
}
