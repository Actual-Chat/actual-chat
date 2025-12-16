using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat.Flows.Infrastructure;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
public sealed partial class FlowResumeEvent(FlowId flowId)
    : IDelegatingCommand<long>, IBackendCommand, IOperationEventSource, IHasDelayUntil, IHasDelayQuanta
{
    private static readonly UuidGenerator UuidGenerator = UlidUuidGenerator.Instance;

    private readonly FlowHub? _hub; // Used only in Schedule method

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public FlowId FlowId { get; init; } = flowId;
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public bool MustReset { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public Moment DelayUntil { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public TimeSpan DelayQuanta { get; set; }

    public FlowResumeEvent(FlowId flowId, FlowHub? hub) : this(flowId)
        => _hub = hub;

    public override string ToString()
    {
        var delayUntilPart = DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "";
        if (delayUntilPart.Length > 0 && DelayQuanta > TimeSpan.Zero)
            delayUntilPart += $" mod {DelayQuanta.ToShortString()}";
        var mustResetPart = MustReset ? $", {nameof(MustReset)} = true" : "";
        return $"{nameof(FlowResumeEvent)}(`{FlowId}`{delayUntilPart}{mustResetPart})";
    }

    public OperationEvent ToOperationEvent()
    {
        var operationEvent = new OperationEvent("", this);
        if (DelayQuanta > TimeSpan.Zero) {
            var uuidPrefix = $"{nameof(FlowResumeEvent)}({FlowId.Value})";
            operationEvent.SetDelayUntil(DelayUntil, DelayQuanta, uuidPrefix);
        }
        else {
            operationEvent.Uuid = $"{nameof(FlowResumeEvent)}-{UuidGenerator.Next()}";
            operationEvent.DelayUntil = DelayUntil;
        }
        return operationEvent;
    }

    // WithXxx methods

    public FlowResumeEvent WithDelay(Moment delayUntil, TimeSpan delayQuanta = default)
    {
        DelayUntil = delayUntil;
        DelayQuanta = delayQuanta;
        return this;
    }

    public FlowResumeEvent WithReset(bool mustReset = true)
    {
        MustReset = mustReset;
        return this;
    }

    // Schedule

    public Task Schedule(CancellationToken cancellationToken = default)
        => _hub.Require().Schedule(this, cancellationToken);

    public Task Schedule(FlowHub hub, CancellationToken cancellationToken = default)
        => hub.Schedule(this, cancellationToken);
}
