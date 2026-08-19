using ActualChat.Queues;
using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Generators;

namespace ActualChat.Flows.Infrastructure;

[DataContract, MessagePackObject(AllowPrivate = true)]
public sealed partial class FlowResumeEvent :
    IDelegatingCommand<long>, IBackendCommand,
    IHasDelayUntil, IHasDelayQuanta,
    ITimeoutProvider,
    IOperationEventSource
{
    private static readonly UuidGenerator UuidGenerator = UlidUuidGenerator.Instance;

    [IgnoreDataMember, IgnoreMember]
    private readonly FlowHub? _hub; // Used only in Schedule method
    [IgnoreDataMember, IgnoreMember]
    private volatile OperationEvent? _operationEvent;

    [DataMember(Order = 0), Key(0)]
    public FlowId FlowId { get; }
    [DataMember(Order = 1), Key(1)]
    public bool MustReset { get; private set; }
    [DataMember(Order = 2), Key(2)]
    public Moment DelayUntil { get; private set; }

    [DataMember(Order = 3), Key(3)]
    public TimeSpan? DelayQuanta {
        get => MustReset ? TimeSpan.Zero : field; // MustReset overrides DelayQuanta: we can't skip such events
        private set => field = value is { } q ? q.Positive() : null;
    }

    [method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    private FlowResumeEvent(FlowId flowId)
        => FlowId = flowId;

    internal FlowResumeEvent(FlowId flowId, FlowHub hub) : this(flowId)
    {
        _hub = hub;
        DelayQuanta = hub.Defs.ByName[flowId.Name].DelayQuanta;
    }

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

    OperationEvent IOperationEventSource.ToOperationEvent(IServiceProvider? services)
    {
        if (_operationEvent is { } operationEvent)
            return operationEvent;

        // Compute delay quanta
        var hub = _hub ?? services?.FlowHub();
        var delayQuanta = DelayQuanta ?? AutoDelayQuanta.For(DelayUntil - hub.Require().SystemNow);
        var delay = (DelayUntil - hub.Require().SystemNow).Positive();

        // Produce operation event.
        // Quantizing an immediate resume would give every immediate resume of this flow one shared
        // Uuid - a constant "-at-0" when DelayUntil was never set - which FlushEvents skips and the
        // queue dedups by. Only a resume scheduled ahead has a slot to coalesce into.
        operationEvent = new OperationEvent("", this);
        if (delayQuanta > TimeSpan.Zero && delay > TimeSpan.Zero) {
            var uuidPrefix = $"{nameof(FlowResumeEvent)}({FlowId.Value})";
            operationEvent.SetDelayUntil(DelayUntil, delayQuanta, uuidPrefix);
            DelayUntil = operationEvent.DelayUntil; // The slot is the schedule - keep the two in sync
        }
        else {
            operationEvent.Uuid = $"{nameof(FlowResumeEvent)}-{UuidGenerator.Next()}";
            operationEvent.SetDelayUntil(DelayUntil);
        }

        // We produce it just once
        return Interlocked.CompareExchange(ref _operationEvent, operationEvent, null) ?? operationEvent;
    }

    TimeSpan ITimeoutProvider.GetTimeout(IServiceProvider services)
    {
        var flowHub = services.FlowHub();
        var flowDef = flowHub.Defs.ByName[FlowId.Name];
        return flowDef.ResumeTimeout;
    }
}
