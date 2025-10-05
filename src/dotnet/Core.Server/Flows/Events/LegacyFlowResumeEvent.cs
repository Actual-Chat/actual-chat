using ActualChat.Flows.Infrastructure;
using ActualLab.CommandR.Operations;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record LegacyFlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] bool IsHardResume = false,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null,
    [property: DataMember(Order = 13), MemoryPackOrder(13)] Moment? MaxLastRunAt = null,
    [property: DataMember(Order = 12), MemoryPackOrder(12)] Moment DelayUntil = default
) : ILegacyFlowControlEvent, IHasDelayUntil
{
    public override string ToString()
        => $"{nameof(LegacyFlowResumeEvent)}(`{FlowId}`{(IsHardResume ? $", {nameof(IsHardResume)} = true" : "")}{(Tag != null ? $", '{Tag}'" : "")}{(DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "")}{(MaxLastRunAt != null ? $", {nameof(MaxLastRunAt)} = {MaxLastRunAt}" : "")})";

    public Symbol GetNextStep(LegacyFlow flow)
    {
        if (flow is IHasLastRunAt f && f.LastRunAt >= MaxLastRunAt)
            return default; // skip

        return IsHardResume
            ? LegacyFlowSteps.OnHardResume
            : flow.Step;
    }
}
