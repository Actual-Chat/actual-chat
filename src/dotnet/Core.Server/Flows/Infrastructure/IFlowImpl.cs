namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl : IHasId<FlowId>
{
    long Version { get; set; }
    IResult? UntypedResult { get; set; }
    FlowConsole Console { get; set; }

    void SetProperties(FlowId id, long version, IResult? untypedResult, FlowConsole flowConsole);
    Task OnResume(IServiceProvider services, CancellationToken cancellationToken);
}
