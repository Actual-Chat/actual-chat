namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl : IHasId<FlowId>
{
    IServiceProvider? Services { get; set; }

    void Initialize(FlowId id, long version, IServiceProvider? services = null);
    Task Resume(CancellationToken cancellationToken);
}
