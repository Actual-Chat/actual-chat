namespace ActualChat.Flows.Infrastructure;

public interface ILegacyFlowImpl : IFlowImpl
{
    FlowHost Host { get; }
    LegacyFlowWorklet Worklet { get; }
    LegacyFlowEventBin Event { get; }
    Symbol Step { get; }
    Moment? HardResumeAt { get; }

    void SetProperties(
        FlowId id,
        long version,
        Symbol step,
        Moment? hardResumeAt = null,
        LegacyFlowWorklet? worklet = null);
}
