using System.Diagnostics.CodeAnalysis;

namespace ActualChat.Flows;

[StructLayout(LayoutKind.Auto)]
public sealed class FlowEventBin(Flow flow, IFlowEvent @event)
{
    public Flow Flow { get; } = flow;
    public bool IsHandled { get; private set; }
    public IFlowEvent Event { get; } = @event;

    public bool MarkHandled(bool isUsed = true)
        => IsHandled = isUsed;

    public TEvent Require<TEvent>()
        where TEvent : class
        => Is<TEvent>(out var @event)
            ? @event
            : throw Internal.Errors.NoEvent(Flow.GetType(), Flow.Step, typeof(TEvent));

    public IFlowEvent Require<TEvent1, TEvent2>()
        where TEvent1 : class
        where TEvent2 : class
        => Is<TEvent1>(out _) ? Event
            : Is<TEvent2>(out _) ? Event
            : throw Internal.Errors.NoEvent(Flow.GetType(), Flow.Step, typeof(TEvent1), typeof(TEvent2));

    public IFlowEvent Require<TEvent1, TEvent2, TEvent3>()
        where TEvent1 : class
        where TEvent2 : class
        where TEvent3 : class
        => Is<TEvent1>(out _) ? Event
            : Is<TEvent2>(out _) ? Event
            : Is<TEvent3>(out _) ? Event
            : throw Internal.Errors.NoEvent(Flow.GetType(), Flow.Step, typeof(TEvent1), typeof(TEvent2), typeof(TEvent3));

    public bool Is<TEvent>([NotNullWhen(true)] out TEvent? @event)
        where TEvent : class
    {
        if (Event is TEvent e) {
            @event = e;
            MarkHandled();
            return true;
        }

        @event = default!;
        return false;
    }

    public bool Is<TEvent1, TEvent2>([NotNullWhen(true)] out IFlowEvent? @event)
        where TEvent1 : class
        where TEvent2 : class
    {
        @event = Is<TEvent1>(out _) || Is<TEvent2>(out _) ? Event : null;
        return @event != null;
    }

    public bool Is<TEvent1, TEvent2, TEvent3>([NotNullWhen(true)] out IFlowEvent? @event)
        where TEvent1 : class
        where TEvent2 : class
        where TEvent3 : class
    {
        @event = Is<TEvent1>(out _) || Is<TEvent2>(out _) || Is<TEvent3>(out _) ? Event : null;
        return @event != null;
    }

    public TEvent? As<TEvent>()
        where TEvent : class
        => Is<TEvent>(out var @event)
            ? @event
            : null;
}
