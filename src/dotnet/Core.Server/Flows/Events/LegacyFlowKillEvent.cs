using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record LegacyFlowKillEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null
) : ILegacyFlowControlEvent
{
    public override string ToString()
        => $"{nameof(LegacyFlowKillEvent)}(`{FlowId}`{(Tag != null ? $", '{Tag}'" : "")})";

    public Symbol GetNextStep(LegacyFlow flow)
        => flow.Step != LegacyFlowSteps.OnEnding && flow.Step != LegacyFlowSteps.OnEnd
            ? LegacyFlowSteps.OnEnding
            : default;
}
