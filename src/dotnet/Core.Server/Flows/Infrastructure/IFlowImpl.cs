namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl : IHasId<FlowId>
{
    long Version { get; set; }
    int DataVersion { get; set; }
    IResult? UntypedResult { get; set; }
    FlowConsole Console { get; set; }
    FlowRuntime? Runtime { get; }

    void SetProperties(FlowId id, long version, int dataVersion, IResult? untypedResult, FlowConsole flowConsole);
    Task HandleResume(FlowHub hub, string? resetReason, CancellationToken cancellationToken);
}
