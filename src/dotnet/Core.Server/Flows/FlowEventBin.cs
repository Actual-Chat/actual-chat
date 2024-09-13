namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public sealed class FlowEventBin(Flow flow, IFlowEvent @event)
{
    public Flow Flow { get; } = flow;
    public bool IsUsed { get; private set; }
    public IFlowEvent Event { get; } = @event;

    public TEvent Require<TEvent>()
        where TEvent : class
        => Is<TEvent>(out var @event)
            ? @event
            : throw Internal.Errors.NoEvent(Flow.GetType(), Flow.Step, typeof(TEvent));

    public TEvent? As<TEvent>()
        where TEvent : class
        => Is<TEvent>(out var @event)
            ? @event
            : null;

    public bool Is<TEvent>(out TEvent @event)
        where TEvent : class
    {
        if (Event is TEvent e) {
            @event = e;
            MarkUsed();
            return true;
        }

        @event = default!;
        return false;
    }

    public bool MarkUsed(bool isUsed = true)
        => IsUsed = isUsed;
}
