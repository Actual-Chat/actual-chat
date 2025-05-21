using System.Text;
using ActualChat.Flows.Infrastructure;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] bool IsHardResume = false,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] string? Tag = null,
    [property: DataMember(Order = 13), MemoryPackOrder(13)] Moment? MaxLastRunAt = null,
    [property: DataMember(Order = 12), MemoryPackOrder(12)] Moment? DelayUntil = null
) : IFlowControlEvent, IDelayed
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(FlowResumeEvent));
        if (IsHardResume)
            builder.Append(nameof(IsHardResume)).Append(" = true");
        if (Tag != null)
            builder.Append($", '{Tag}'");
        if (DelayUntil != null)
            builder.Append($", {nameof(DelayUntil)} = {DelayUntil}");
        if (MaxLastRunAt != null)
            builder.Append($", {nameof(MaxLastRunAt)} = {MaxLastRunAt}");
        return true;
    }

    public Symbol GetNextStep(Flow flow)
    {
        if (flow is IHasLastRunAt f && f.LastRunAt >= MaxLastRunAt)
            return default; // skip

        return IsHardResume
            ? FlowSteps.OnHardResume
            : flow.Step;
    }
}
