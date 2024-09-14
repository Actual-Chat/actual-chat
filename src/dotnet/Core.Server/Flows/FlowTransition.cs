using ActualLab.CommandR.Operations;
using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct FlowTransition(Flow Flow, Symbol Step)
{
    public bool MustStore { get; init; } = true;
    public bool MustResume { get; init; } = false;
    public ImmutableList<OperationEvent> Events { get; init; } = ImmutableList<OperationEvent>.Empty;

    public bool EffectiveMustStore
        => MustStore || Step == FlowSteps.OnEnded || Events != null;

    public override string ToString()
    {
        var flags = (EffectiveMustStore, MustResume) switch {
            (true, true) => "store, resume",
            (true, false) => "store",
            (false, true) => "no-store, resume",
            (false, false) => "no-store",
        };
        return $"{nameof(FlowTransition)}('{Step}', {flags}, {Events.Count} event(s))";
    }

    public FlowTransition AddEvent(OperationEvent @event)
        => this with {
            MustStore = true,
            Events = Events.Add(@event),
        };

    public FlowTransition AddEvents(params OperationEvent[] events)
        => this with {
            MustStore = true,
            Events = Events.AddRange(events),
        };

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
