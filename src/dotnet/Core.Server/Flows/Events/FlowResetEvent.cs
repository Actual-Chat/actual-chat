using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowResetEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null,
    [property: DataMember(Order = 11), MemoryPackOrder(11)] Moment? MaxLastRunAt = null
) : IFlowControlEvent
{
    public override string ToString()
        => $"{nameof(FlowResetEvent)}(`{FlowId}`{(Tag != null ? $", '{Tag}'" : "")})";

    public Symbol GetNextStep(Flow flow)
    {
        if (flow is IHasLastRunAt f && f.LastRunAt >= MaxLastRunAt)
            return default; // skip

        return FlowSteps.OnReset;
    }
}
