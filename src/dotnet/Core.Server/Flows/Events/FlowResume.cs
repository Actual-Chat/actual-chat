using ActualChat.Time;
using MemoryPack;
using MessagePack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public sealed partial class FlowResume(FlowId flowId, Moment delayUntil = default) : IFlowEvent, IHasDelayUntil
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public FlowId FlowId { get; init; } = flowId;
    [DataMember(Order = 10), MemoryPackOrder(10)]
    public Moment DelayUntil { get; set; } = delayUntil;
    [DataMember(Order = 11), MemoryPackOrder(11)]
    public TimeSpan DelayQuanta { get; set; }
    [DataMember(Order = 20), MemoryPackOrder(20)]
    public bool MustRestart { get; set; }

    public override string ToString()
    {
        var delayUntilPart = DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "";
        var mustRestartPart = MustRestart ? $", {nameof(MustRestart)} = true" : "";
        return $"{nameof(FlowResume)}(`{FlowId}`{delayUntilPart}{mustRestartPart})";
    }

    public FlowResume SetDelayUntil(Moment delayUntil)
    {
        DelayUntil = delayUntil;
        return this;
    }

    public FlowResume SetDelayQuanta(TimeSpan delayQuanta)
    {
        DelayQuanta = delayQuanta;
        return this;
    }

    public FlowResume ResetDelayQuanta()
    {
        DelayQuanta = default;
        return this;
    }

    public FlowResume SetMustRestart(bool mustRestart = true)
    {
        MustRestart = mustRestart;
        return this;
    }
}
