using ActualLab.CommandR.Operations;
using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct FlowTransition(Flow Flow, Symbol Step)
{
    public bool MustStore { get; init; } = true;
    public bool MustWait { get; init; } = true;
    public ImmutableList<OperationEvent> Events { get; init; } = ImmutableList<OperationEvent>.Empty;

    public bool EffectiveMustStore
        => MustStore || Step == FlowSteps.OnRemove || Events != null;

    public override string ToString()
    {
        var flags = (EffectiveMustStore, MustWait: MustWait) switch {
            (true, true) => "store, wait",
            (true, false) => "store",
            (false, true) => "no-store, wait",
            (false, false) => "no-store",
        };
        return $"->('{Step}', {flags})";
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
