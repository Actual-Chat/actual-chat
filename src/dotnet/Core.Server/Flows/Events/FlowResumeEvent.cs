using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] bool IsHardResume = false,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null
) : IFlowControlEvent
{
    public override string ToString()
        => $"{nameof(FlowResumeEvent)}(`{FlowId}`{(IsHardResume ? $", {nameof(IsHardResume)} = true" : "")}{(Tag != null ? $", '{Tag}'" : "")})";

    public Symbol GetNextStep(Flow flow)
        => IsHardResume
            ? FlowSteps.OnHardResume
            : flow.Step;
}
