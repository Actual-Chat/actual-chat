namespace ActualChat.Flows.Infrastructure;

public interface ILegacyFlowImpl : IFlowImpl
{
    FlowHost Host { get; }
    LegacyFlowWorklet Worklet { get; }
    LegacyFlowEventBin Event { get; }

    void Initialize(
        FlowId id,
        long version,
        Symbol step,
        Moment? hardResumeAt = null,
        LegacyFlowWorklet? worklet = null);
}
