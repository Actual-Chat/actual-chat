using System.Diagnostics.Tracing;
using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows.Builder;

public delegate Task<IFlowEvent> TEventHandler<T>(T @event, CancellationToken cancellationToken);
public sealed class NoHandlerSet {
    public sealed class FlowEvent: IFlowEvent {
        public FlowId FlowId { get; init; }
        public FlowEvent(){
            FlowId = FlowId.None;
        }
    }
    public  static async Task<IFlowEvent> DoNothing(NoHandlerSet _, CancellationToken _cancellationToken) {
        return new NoHandlerSet.FlowEvent();
    }
}


public class FlowBuilder: FlowBuilder<NoHandlerSet, NoHandlerSet, NoHandlerSet, NoHandlerSet>{
    public FlowBuilder(): base (
        NoHandlerSet.DoNothing,
        NoHandlerSet.DoNothing,
        NoHandlerSet.DoNothing,
        NoHandlerSet.DoNothing
    ) {}
    public FlowBuilder<TNextEvent> On<TNextEvent>(TEventHandler<TNextEvent> onEventFn) {
       return new (onEventFn);
    }
}
public class FlowBuilder<TEvent0>(TEventHandler<TEvent0> eventHandler0): 
    FlowBuilder<TEvent0, NoHandlerSet, NoHandlerSet, NoHandlerSet>(eventHandler0, NoHandlerSet.DoNothing, NoHandlerSet.DoNothing, NoHandlerSet.DoNothing)
{
    public FlowBuilder<TEvent0, TNextEvent> On<TNextEvent>(TEventHandler<TNextEvent> onEventFn) {
       return new (eventHandler0, onEventFn);
    }
}


public class FlowBuilder<TEvent0, TEvent1>(
    TEventHandler<TEvent0> eventHandler0,
    TEventHandler<TEvent1> eventHandler1
): 
    FlowBuilder<TEvent0, TEvent1, NoHandlerSet, NoHandlerSet>(
        eventHandler0, eventHandler1, NoHandlerSet.DoNothing, NoHandlerSet.DoNothing
    )
{
    public FlowBuilder<TEvent0, TEvent1, TNextEvent> On<TNextEvent>(TEventHandler<TNextEvent> onEventFn) {
       return new (eventHandler0, eventHandler1, onEventFn);
    }
}

public class FlowBuilder<TEvent0, TEvent1, TEvent2>(
    TEventHandler<TEvent0> eventHandler0,
    TEventHandler<TEvent1> eventHandler1,
    TEventHandler<TEvent2> eventHandler2
): 
    FlowBuilder<TEvent0, TEvent1, TEvent2, NoHandlerSet>(
        eventHandler0, eventHandler1, eventHandler2, NoHandlerSet.DoNothing
    )
{
    public FlowBuilder<TEvent0, TEvent1, TEvent2, TNextEvent> On<TNextEvent>(TEventHandler<TNextEvent> onEventFn) {
       return new (eventHandler0, eventHandler1, eventHandler2, onEventFn);
    }
}

public class FlowBuilder<TEvent0, TEvent1, TEvent2, TEvent3>(
    TEventHandler<TEvent0> eventHandler0,
    TEventHandler<TEvent1> eventHandler1,
    TEventHandler<TEvent2> eventHandler2,
    TEventHandler<TEvent3> eventHandler3
)
{
    [EventHandler]
    public async Task OnEvent(TEvent0 @event, CancellationToken cancellationToken)
        => await eventHandler0(@event, cancellationToken).ConfigureAwait(false);

    [EventHandler]
    public async Task OnEvent(TEvent1 @event, CancellationToken cancellationToken)
        => await eventHandler1(@event, cancellationToken).ConfigureAwait(false);


    [EventHandler]
    public async Task OnEvent(TEvent2 @event, CancellationToken cancellationToken)
        => await eventHandler2(@event, cancellationToken).ConfigureAwait(false);

    [EventHandler]
    public async Task OnEvent(TEvent3 @event, CancellationToken cancellationToken)
        => await eventHandler3(@event, cancellationToken).ConfigureAwait(false);

    private async Task Dispatch<T>(TEventHandler<T> handler, T @event, CancellationToken cancellationToken) {
        var flowEvent = await handler(@event, cancellationToken).ConfigureAwait(false);
        this.flow.
    }

}
