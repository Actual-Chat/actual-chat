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
    [DataMember(Order = 105), MemoryPackOrder(105)]
    public Moment? NextRecheckAt { get; protected set; }
    [DataMember(Order = 103), MemoryPackOrder(103)]
    public long FlowSetVersion { get; protected set; }
    [IgnoreDataMember, MemoryPackIgnore]
    protected abstract int CurrentFlowSetVersion { get; }

    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan WatchdogInterval { get; } = TimeSpan.FromHours(24);
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan RecheckInterval { get; } = TimeSpan.FromSeconds(10);
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan TimerRescheduleThreshold { get; } = TimeSpan.FromSeconds(1);

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        if (!await OnBeforeFirstIndexAfterReset(cancellationToken).ConfigureAwait(false))
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);

        return Resume(nameof(OnIndex));
    }

    protected virtual Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
    {
        if (FlowSetVersion < CurrentFlowSetVersion)
            Cursor = default; // needs reindex from the beginning
        return ActualLab.Async.TaskExt.TrueTask;
    }

    protected abstract Task<BatchIndexingResult<TCursor>> Process(TCursor? cursor, CancellationToken cancellationToken);

    protected virtual async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var (mustEnd, isTailReached, updatedCursor, processedCount) = await Process(Cursor, cancellationToken).ConfigureAwait(false);
        Cursor = updatedCursor;
        var transitionKind = IndexingFlowTransitionKind.Resume;
        Log.LogInformation(
            "`{Id}`.OnIndex: processed portion: MustEnd={MustEnd}, IsTailReached={IsTailReached}, {@UpdatedCursor}",
            Id,
            mustEnd,
            isTailReached,
            updatedCursor);
        if (isTailReached) {
            FlowSetVersion = CurrentFlowSetVersion;
            transitionKind = await HandleTail(processedCount, cancellationToken).ConfigureAwait(false);
        }
        if (transitionKind != IndexingFlowTransitionKind.Recheck)
            NextRecheckAt = null;
        if (mustEnd)
            transitionKind = IndexingFlowTransitionKind.Suspend;

        Event.MarkHandled();
        return transitionKind switch {
            IndexingFlowTransitionKind.Resume => QueueResume(nameof(OnIndex), "Continue processing when possible"),
            IndexingFlowTransitionKind.Watchdog => WaitForWatchdog(),
            IndexingFlowTransitionKind.Recheck => WaitForRecheck(),
            IndexingFlowTransitionKind.Suspend => WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt),
            _ => throw new ArgumentOutOfRangeException(nameof(transitionKind), transitionKind, null),
        };
    }

    protected virtual Task<IndexingFlowTransitionKind> HandleTail(int processCount, CancellationToken cancellationToken)
    {
        Log.LogInformation("`{Id}`.OnTailReached: {Cursor}", Id, Cursor);
        var transitionKind = processCount > 0
            || NextRecheckAt is null
            || NextRecheckAt < Clocks.SystemClock.Now + TimerRescheduleThreshold
                ? IndexingFlowTransitionKind.Recheck
                : IndexingFlowTransitionKind.Watchdog;
        return Task.FromResult(transitionKind);
    }

    private FlowTransition WaitForWatchdog()
    {
        if (GetNextWatchdogAt() is { } nextWatchdogAt) {
            NextWatchdogTimerAt = nextWatchdogAt;
            Log.LogInformation("`{Id}`.WaitForWatchdog: Waiting for watchdog timer at {NextTimerAt}", Id, nextWatchdogAt);
            return WaitForTimer(nameof(OnIndex), nextWatchdogAt, "Waiting for watchdog timer");
        }

        Log.LogInformation("`{Id}`.WaitForWatchdog: watchdog was already set", Id);
        return default;
    }

    private FlowTransition WaitForRecheck()
    {
        if (GetNextRecheckAt() is { } nextRecheckAt) {
            NextRecheckAt = nextRecheckAt;
            Log.LogInformation("`{Id}`.WaitForRecheck: Waiting for recheck at {NextTimerAt}", Id, nextRecheckAt);
            return WaitForTimer(nameof(OnIndex), nextRecheckAt, "Waiting for recheck");
        }

        Log.LogInformation("`{Id}`.WaitForRecheck: recheck was already set", Id);
        return default;
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

    private Moment? GetNextRecheckAt()
    {
        var now = Clocks.SystemClock.Now;
        if (NextRecheckAt == null)
            return now + RecheckInterval;

        if (NextRecheckAt <= now + TimerRescheduleThreshold)
            return now + RecheckInterval;

        return null;
    }
}
