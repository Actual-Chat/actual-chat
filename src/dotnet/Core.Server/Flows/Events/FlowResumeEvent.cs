using ActualChat.Time;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record FlowResumeEvent(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] FlowId FlowId,
    [property: DataMember(Order = 10), MemoryPackOrder(10)] Moment DelayUntil = default,
    [property: DataMember(Order = 11), MemoryPackOrder(11)] TimeSpan DelayQuanta = default,
    [property: DataMember(Order = 20), MemoryPackOrder(20)] bool MustReset = false
) : IFlowEvent, IHasDelayUntil
{
    public override string ToString()
    {
        var delayUntilPart = DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "";
        var mustRestartPart = MustReset ? $", {nameof(MustReset)} = true" : "";
        return $"{nameof(FlowResumeEvent)}(`{FlowId}`{delayUntilPart}{mustRestartPart})";
    }
}
