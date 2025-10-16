namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl : IHasId<FlowId>
{
    void Initialize(FlowId id, long version);
    Task Resume(FlowRuntime runtime, CancellationToken cancellationToken);
}
