using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record LegacyFlowStartEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId
) : ILegacyFlowControlEvent
{
    public override string ToString()
        => $"{nameof(LegacyFlowStartEvent)}(`{FlowId}`)";

    public Symbol GetNextStep(LegacyFlow flow)
        => flow.Step == LegacyFlowSteps.Starting
            ? LegacyFlowSteps.OnReset
            : default; // Skip the event
}
