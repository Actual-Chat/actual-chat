namespace ActualChat.Flows.Infrastructure;

public interface IFlowImpl
{
    FlowHost Host { get; }
    FlowWorklet Worklet { get; }
    FlowEventBin Event { get; }
}
