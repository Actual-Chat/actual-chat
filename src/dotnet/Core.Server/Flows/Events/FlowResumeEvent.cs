using ActualLab.CommandR.Operations;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] Moment DelayUntil = default
) : IFlowEvent, IHasDelayUntil
{
    public override string ToString()
        => $"{nameof(FlowResumeEvent)}(`{FlowId}`{(DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "")})";
}
