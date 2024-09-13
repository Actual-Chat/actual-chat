using ActualLab.CommandR.Operations;
using ActualChat.Flows.Infrastructure;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public readonly record struct FlowTransition(Flow Flow, Symbol Step)
{
    public bool MustStore { get; init; } = true;
    public bool MustWait { get; init; } = true;
    public Action<Operation>? EventBuilder { get; init; }

    public bool EffectiveMustStore
        => MustStore || Step == FlowSteps.OnRemove || EventBuilder != null;

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

    public FlowTransition AddEvents(Action<Operation> eventBuilder)
    {
        var oldEventBuilder = EventBuilder;
        return this with {
            MustStore = true,
            EventBuilder = operation => {
                oldEventBuilder?.Invoke(operation);
                eventBuilder.Invoke(operation);
            },
        };
    }

    public FlowTransition AddTimerEvent(TimeSpan delay, string? tag = null)
    {
        var e = new FlowTimerEvent(Flow.Id, tag);
        return AddEvents(o => o.AddEvent(e, delay));
    }

    public FlowTransition AddTimerEvent(Moment firesAt, string? tag = null)
    {
        var e = new FlowTimerEvent(Flow.Id, tag);
        return AddEvents(o => o.AddEvent(e, firesAt));
    }
}
