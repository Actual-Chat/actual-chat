namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl : IHasId<FlowId>
{
    long Version { get; set; }
    IResult? UntypedResult { get; set; }

    void SetProperties(FlowId id, long version, IResult? untypedResult);
    Task OnResume(IServiceProvider services, CancellationToken cancellationToken);
}
