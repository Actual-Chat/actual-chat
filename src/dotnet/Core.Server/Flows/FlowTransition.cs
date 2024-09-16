using ActualLab.CommandR.Operations;
using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct FlowTransition
{
    public Flow Flow { get; init; }
    public Symbol Step { get; init; }
    public bool MustStore { get; init; }
    public Moment? HardResumeAt { get; init; }
    public ImmutableList<OperationEvent> Events { get; init; } = ImmutableList<OperationEvent>.Empty;

    public bool EffectiveMustStore
        => MustStore || Step == FlowSteps.Removed || Events.Count != 0;

    // ReSharper disable once ConvertToPrimaryConstructor
    public FlowTransition(Flow flow, Symbol step, Moment? hardResumeAt = default, OperationEvent? @event = null)
    {
        Flow = flow;
        Step = step;
        HardResumeAt = hardResumeAt;
        if (@event != null)
            Events = Events.Add(@event);
    }

    // ReSharper disable once ConvertToPrimaryConstructor
    public FlowTransition(Flow flow, Symbol step, Moment? hardResumeAt = default, params OperationEvent[] @events)
    {
        Flow = flow;
        Step = step;
        HardResumeAt = hardResumeAt;
        if (events.Length != 0)
            Events = Events.AddRange(events);
    }

    public override string ToString()
    {
        var mustStore = EffectiveMustStore ? "store" : "no-store";
        var hardResumeDelay = HardResumeAt is { } hardResumeAt
            ? hardResumeAt - SystemClock.Instance.Now
            : default;
        var sHardResumeAt = hardResumeDelay > TimeSpan.Zero
            ? hardResumeDelay.ToShortString()
            : "now";
        return $"{nameof(FlowTransition)}('{Step}', {mustStore}, resumes in: {sHardResumeAt}, {Events.Count} event(s))";
    }

    public FlowTransition AddEvents(OperationEvent @event)
        => this with { Events = Events.Add(@event) };

    public FlowTransition AddEvents(params OperationEvent[] events)
        => this with { Events = Events.AddRange(events) };

    public FlowTransition AddTimerEvent(TimeSpan delay, string? tag = null)
    {
        var clock = ((IFlowImpl)Flow).Worklet.Host.Clocks.SystemClock;
        return AddTimerEvent(clock.Now + delay, tag);
    }

    public FlowTransition AddTimerEvent(Moment firesAt, string? tag = null)
    {
        var e = new FlowTimerEvent(Flow.Id, tag);
        return AddEvents(new OperationEvent(firesAt, e));
    }
}
