using ActualChat.Queues;
using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat.Flows.Infrastructure;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class FlowResumeEvent : IDelegatingCommand<long>, IBackendCommand,
    IOperationEventSource, IHasUuid,
    IHasDelayUntil, IHasDelayQuanta, ITimeoutProvider
{
    private static readonly UuidGenerator UuidGenerator = UlidUuidGenerator.Instance;

    private readonly FlowHub? _hub; // Used only in Schedule method
    private volatile OperationEvent? _operationEvent;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public FlowId FlowId { get; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public bool MustReset { get; private set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public Moment DelayUntil { get; private set; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public TimeSpan? DelayQuanta { get; private set => field = value is { } q ? q.Positive() : null; }

    [method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    private FlowResumeEvent(FlowId flowId)
        => FlowId = flowId;

    internal FlowResumeEvent(FlowId flowId, FlowHub hub) : this(flowId)
        => _hub = hub;

    public override string ToString()
    {
        var delayUntilPart = DelayUntil != default ? $", {nameof(DelayUntil)} = {DelayUntil}" : "";
        if (delayUntilPart.Length > 0 && DelayQuanta > TimeSpan.Zero)
            delayUntilPart += $" mod {DelayQuanta.ToShortString("auto")}";
        var mustResetPart = MustReset ? $", {nameof(MustReset)} = true" : "";
        return $"{nameof(FlowResumeEvent)}(`{FlowId}`{delayUntilPart}{mustResetPart})";
    }

    // WithXxx methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FlowResumeEvent WithDelay(Moment delayUntil)
    {
        ThrowIfImmutable();
        DelayUntil = delayUntil;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FlowResumeEvent WithDelay(Moment delayUntil, TimeSpan? delayQuanta)
    {
        WithDelay(delayUntil);
        DelayQuanta = delayQuanta;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FlowResumeEvent WithDelay(TimeSpan delay)
    {
        ThrowIfImmutable();
        var delayUntil = _hub?.Clocks.SystemClock.Now + delay ?? Moment.Now + delay;
        DelayUntil = delayUntil;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FlowResumeEvent WithDelay(TimeSpan delay, TimeSpan? delayQuanta)
    {
        WithDelay(delay);
        DelayQuanta = delayQuanta;
        return this;
    }

    public FlowResumeEvent WithDelayQuanta(TimeSpan? delayQuanta)
    {
        ThrowIfImmutable();
        DelayQuanta = delayQuanta;
        return this;
    }

    public FlowResumeEvent WithReset(bool mustReset = true)
    {
        ThrowIfImmutable();
        MustReset = mustReset;
        return this;
    }

    // Schedule

    public Task Schedule(CancellationToken cancellationToken = default)
        => _hub.Require().Schedule(this, cancellationToken);

    // Private methods

    private void ThrowIfImmutable()
    {
        if (_operationEvent is not null)
            throw StandardError.Internal($"This {nameof(FlowResumeEvent)} instance is already immutable.");
    }

    // Explicit interface implementations

    string IHasUuid.Uuid => field ??= ((IOperationEventSource)this).ToOperationEvent(null!).Uuid;

    OperationEvent IOperationEventSource.ToOperationEvent(IServiceProvider services)
    {
        if (_operationEvent is { } operationEvent)
            return operationEvent;

        operationEvent = new OperationEvent("", this);
        var delayQuanta = DelayQuanta ?? AutoDelayQuanta.For(DelayUntil - _hub.Require().Clocks.SystemClock.Now);
        if (delayQuanta > TimeSpan.Zero) {
            var uuidPrefix = $"{nameof(FlowResumeEvent)}({FlowId.Value})";
            operationEvent.SetDelayUntil(DelayUntil, delayQuanta, uuidPrefix);
        }
        else {
            operationEvent.Uuid = $"{nameof(FlowResumeEvent)}-{UuidGenerator.Next()}";
            operationEvent.SetDelayUntil(DelayUntil);
        }

        // We compute it maximum once
        return Interlocked.CompareExchange(ref _operationEvent, operationEvent, null) ?? operationEvent;
    }

    TimeSpan ITimeoutProvider.GetTimeout(IServiceProvider services)
    {
        var flowHub = services.FlowHub();
        var flowDef = flowHub.Defs.ByName[FlowId.Name];
        return flowDef.ResumeTimeout;
    }
}
