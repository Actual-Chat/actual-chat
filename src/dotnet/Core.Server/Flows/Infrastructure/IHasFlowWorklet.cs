namespace ActualChat.Flows.Infrastructure;

public interface IHasFlowWorklet
{
    FlowHost Host { get; }
    FlowWorklet Worklet { get; }
    FlowEventBin Event { get; }
}
