using MemoryPack;

namespace ActualChat.Flows;

public abstract partial class PeriodicFlow : Flow
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected TimeSpan MaxDelay { get; set; } = TimeSpan.FromDays(7);
    [IgnoreDataMember, MemoryPackIgnore]
    protected string? EndReason { get; set; }

    [DataMember(Order = 100), MemoryPackOrder(100)]
    public Moment LastRunAt { get; protected set; }
    [DataMember(Order = 101), MemoryPackOrder(101)]
    public Moment? NextRunAt { get; protected set; }
    [DataMember(Order = 110), MemoryPackOrder(110)]
    public int RunCount { get; protected set; }

    // Not a step!
    protected abstract Task<string?> Update(CancellationToken cancellationToken);
    protected abstract Task Run(CancellationToken cancellationToken);

    protected override Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        LastRunAt = Clocks.SystemClock.Now;
        NextRunAt = null;
        return GetTransition(cancellationToken);
    }

    protected async Task<FlowTransition> OnCheck(CancellationToken cancellationToken)
    {
        Log.LogInformation("`{Id}`: OnCheck, Event: {Event}", Id, Event.Event);
        if (!Event.IsHandled)
            Event.Require<FlowTimerEvent>();

        var transition = await GetTransition(cancellationToken).ConfigureAwait(false);
        if (transition.Step != nameof(OnCheck) || transition.HardResumeAt.HasValue)
            return transition;

        Log.LogInformation("`{Id}`: Run #{RunIndex}", Id, RunCount);
        await Run(cancellationToken).ConfigureAwait(false);
        LastRunAt = Clocks.SystemClock.Now;
        NextRunAt = null;
        RunCount++;
        return await GetTransition(cancellationToken).ConfigureAwait(false);
    }

    protected virtual async Task<FlowTransition> GetTransition(CancellationToken cancellationToken)
    {
        EndReason = await Update(cancellationToken).ConfigureAwait(false);
        if (!EndReason.IsNullOrEmpty()) {
            Log.LogWarning("`{Id}`: {EndReason}, will end", Id, EndReason);
            return End();
        }

        var now = Clocks.SystemClock.Now;
        if (NextRunAt is { } lastNextRunAt)
            return lastNextRunAt <= now
                ? Resume(nameof(OnCheck))
                : WaitForEvent(nameof(OnCheck), lastNextRunAt);

        var nextRunAt = now + (ComputeNextRunAt(now, cancellationToken) - now).Clamp(TimeSpan.Zero, MaxDelay);
        NextRunAt = nextRunAt;
        Log.LogInformation("`{Id}`: Next run in: {Delay}", Id, (nextRunAt - now).ToShortString());
        return WaitForTimer(nameof(OnCheck), nextRunAt);
    }

    protected abstract Moment ComputeNextRunAt(Moment now, CancellationToken cancellationToken);
}
