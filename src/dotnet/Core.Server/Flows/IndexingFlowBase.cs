using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

public abstract class IndexingFlowBase<TCursor> : Flow
{
    [DataMember(Order = 100), MemoryPackOrder(100)]
    public TCursor? Cursor { get; private set; }

    [DataMember(Order = 101), MemoryPackOrder(101)]
    public Moment? NextTimerAt { get; private set; }

    [DataMember(Order = 102), MemoryPackOrder(102)]
    public Moment? NextWatchdogTimerAt { get; private set; }

    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan Interval { get; } = TimeSpan.FromSeconds(10);
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan WatchdogInterval { get; } = TimeSpan.FromHours(24);
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan TimerRescheduleThreshold { get; } = TimeSpan.FromSeconds(1);

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
        => Resume(nameof(OnIndex));

    protected abstract Task<BatchIndexingResult<TCursor>> ProcessBatch(TCursor? cursor, CancellationToken cancellationToken);

    protected virtual async Task<FlowTransition> OnIndex(CancellationToken cancellationToken)
    {
        var (mustEnd, isTailReached, updatedCursor) = await ProcessBatch(Cursor, cancellationToken).ConfigureAwait(false);
        Cursor = updatedCursor;
        if (mustEnd)
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt); // i.e. chat is removed

        return isTailReached
            ? GetTransition(WatchdogInterval, NextWatchdogTimerAt, x => NextWatchdogTimerAt = x)
            : GetTransition(Interval, NextTimerAt, x => NextTimerAt = x);
    }

    private bool NeedsTimer(Moment? currentResumeAt, TimeSpan interval, out Moment nextTimerAt)
    {
        var now = Clocks.SystemClock.Now;
        if (currentResumeAt == null) {
            nextTimerAt = now + interval;
            return true;
        }

        if (currentResumeAt <= now + TimerRescheduleThreshold) {
            nextTimerAt = now + interval;
            return true;
        }

        nextTimerAt = Moment.MinValue;
        return false;
    }

    private FlowTransition GetTransition(TimeSpan interval, Moment? resumeAt, Action<Moment> updateResumeAt)
    {
        if (!NeedsTimer(resumeAt, interval, out var nextTimerAt))
            return WaitForEvent(nameof(OnIndex), InfiniteHardResumeAt);

        updateResumeAt(nextTimerAt);
        return WaitForTimer(nameof(OnIndex), nextTimerAt);
    }
}
