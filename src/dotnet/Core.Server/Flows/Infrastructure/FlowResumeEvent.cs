using ActualChat.Queues;
using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat.Flows.Infrastructure;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class FlowResumeEvent : IDelegatingCommand<long>, IBackendCommand, IOperationEventSource, IHasDelayUntil, IHasDelayQuanta, IComputesTimeout
{
    private static readonly UuidGenerator UuidGenerator = UlidUuidGenerator.Instance;

    private readonly FlowHub? _hub; // Used only in Schedule method

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public FlowId FlowId { get; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public bool MustReset { get; private set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public Moment DelayUntil { get; private set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public TimeSpan DelayQuanta { get; private set; }

    [method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    private FlowResumeEvent(FlowId flowId)
        => FlowId = flowId;

    internal FlowResumeEvent(FlowId flowId, FlowHub hub) : this(flowId)
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
            operationEvent.SetDelayUntil(DelayUntil);
        }
        return operationEvent;
    }

    // WithXxx methods

    public FlowResumeEvent WithDelay(Moment delayUntil, TimeSpan? delayQuanta = null)
    {
        DelayUntil = delayUntil;
        return WithQuanta(delayQuanta);
    }

    public FlowResumeEvent WithDelay(TimeSpan delay, TimeSpan? delayQuanta = null)
    {
        var delayUntil = _hub?.Clocks.SystemClock.Now + delay ?? Moment.Now + delay;
        DelayUntil = delayUntil;
        return WithQuanta(delayQuanta);
    }

    public FlowResumeEvent WithQuanta(TimeSpan? delayQuanta = null)
    {
        var delayUntil = DelayUntil;
        if (delayQuanta is null) {
            if (_hub != null) {
                var flowDef = _hub.Defs.ByName[FlowId.Name];
                var delay = (delayUntil - _hub.Clocks.SystemClock.Now).Positive();
                var quanta = delayQuanta ?? flowDef.DelayQuanta ?? flowDef.GetQuant?.Invoke(delay) ?? TimeSpan.Zero;
                DelayQuanta = quanta;
            }
            else
                DelayQuanta = TimeSpan.Zero;
        }
        else
            DelayQuanta = delayQuanta.Value.Positive();
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

    // IComputesTimeout implementation

    TimeSpan IComputesTimeout.ComputeTimeout(IServiceProvider services)
    {
        var flowHub = services.FlowHub();
        var flowDef = flowHub.Defs.ByName[FlowId.Name];
        return flowDef.ResumeTimeout;
    }
}
