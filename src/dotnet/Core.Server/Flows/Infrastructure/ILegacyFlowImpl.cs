namespace ActualChat.Flows.Infrastructure;

public interface ILegacyFlowImpl
{
    FlowHost Host { get; }
    LegacyFlowWorklet Worklet { get; }
    LegacyFlowEventBin Event { get; }
}
