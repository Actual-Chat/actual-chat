namespace ActualChat.Flows;

// Must be an IDelegatingCommand<TResult> to make sure no operation is stored for it, no invalidation, etc.
public interface IFlowEvent : IDelegatingCommand<long>, IBackendCommand
{
    FlowId FlowId { get; init; }
}
