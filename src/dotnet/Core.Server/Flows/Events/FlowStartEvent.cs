using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowStartEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId
) : IFlowControlEvent
{
    public Symbol GetNextStep(Flow flow)
        => flow.Step == FlowSteps.Starting
            ? FlowSteps.OnReset
            : default; // Skip the event
}
