namespace ActualChat.Flows;

public interface IFlowEvent : IApiCommand<Unit>, IBackendCommand
{
    FlowId FlowId { get; init; }
}
