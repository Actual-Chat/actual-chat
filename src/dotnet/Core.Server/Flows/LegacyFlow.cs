using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class LegacyFlow : Flow, ILegacyFlowImpl
{
    public static class Defaults
    {
        public static TimeSpan KeepAliveFor { get; } = TimeSpan.FromSeconds(10);
        public static RetryDelaySeq FailureDelays { get; } = RetryDelaySeq.Exp(0.5, 3);
    }

    public static Moment InfiniteHardResumeAt { get; } = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private LegacyFlowWorklet? _worklet;

    // ILegacyFlowImpl
    FlowHost ILegacyFlowImpl.Host => Worklet.Host;
    LegacyFlowWorklet ILegacyFlowImpl.Worklet => Worklet;
    LegacyFlowEventBin ILegacyFlowImpl.Event => Event;
    protected FlowHost Host => Worklet.Host;
    protected LegacyFlowWorklet Worklet => RequireWorklet();
    protected LegacyFlowEventBin Event { get; private set; } = null!;

    // Most useful service shortcuts
    protected IServiceProvider Services => Host.Services;
    protected MomentClockSet Clocks => Host.Clocks;
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    // Persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol Step { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Moment? HardResumeAt { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan DefaultTimerDelayQuanta { get; protected set; } = TimeSpan.FromSeconds(1);
    [IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan ResumeDelayQuanta { get; protected set; } = TimeSpan.FromSeconds(0.1);

    // Used by FlowWorklet
    [IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan KeepAliveFor { get; set; } = Defaults.KeepAliveFor;
    [IgnoreDataMember, MemoryPackIgnore]
    public RetryDelaySeq FailureDelays { get; set; } = Defaults.FailureDelays;

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}' @ {Step}, v.{Version.FormatVersion()})";

    public override Flow Clone()
    {
        var clone = (LegacyFlow)base.Clone();
        clone._worklet = null;
        return clone;
    }

    public async Task<LegacyFlowTransition> ProcessEvent(IFlowEvent evt, CancellationToken cancellationToken)
    {
        Event = new LegacyFlowEventBin(this, evt);
        var step = Step;
        LegacyFlowTransition transition;
        try {
            if (Event.Is<ILegacyFlowControlEvent>(out var flowControlEvent)) {
                step = flowControlEvent.GetNextStep(this);
                if (step.IsEmpty)
                    return default;
            }
            transition = await InvokeStep(step, cancellationToken).ConfigureAwait(false);

            if (!Event.IsHandled) {
                var error = Errors.UnhandledEvent(GetType(), Step, evt.GetType());
                Log.LogError(error,
                    "`{Id}`.ProcessEvent @ '{Step}': unhandled event '{EventType}'",
                    Id, Step, evt.GetType().GetName());
                throw error;
            }
        }
        catch (Exception ex) when (!ex.IsCancellationOf(cancellationToken)) {
            Event.MarkHandled(false);
            transition = await HandleError(ex, cancellationToken).ConfigureAwait(false);
            if (!Event.IsHandled)
                throw;
        }
        finally {
            Event = null!;
        }
        await ApplyTransition(transition, evt, cancellationToken).ConfigureAwait(false);
        return transition;
    }

    // Initialize

    void ILegacyFlowImpl.SetProperties(FlowId id, long version, Symbol step, Moment? hardResumeAt, LegacyFlowWorklet? worklet)
        => SetProperties(id, version, step, hardResumeAt, worklet);
    protected void SetProperties(FlowId id, long version, Symbol step, Moment? hardResumeAt = null, LegacyFlowWorklet? worklet = null)
    {
        base.SetProperties(id, version, null);
        _worklet = worklet;
        Step = step;
        HardResumeAt = hardResumeAt;
    }

    protected override Task Resume(FlowRuntime runtime, CancellationToken cancellationToken)
        => throw StandardError.Internal($"{nameof(Resume)} should never be called on a {nameof(LegacyFlow)}.");

    // Default steps

    protected abstract Task<LegacyFlowTransition> OnReset(CancellationToken cancellationToken);

    protected virtual Task<LegacyFlowTransition> OnHardResume(CancellationToken cancellationToken)
        => InvokeStep(Step, cancellationToken);

    protected Task<LegacyFlowTransition> OnEnding(CancellationToken cancellationToken)
    {
        Event.MarkHandled();
        Log.LogInformation("`{Id}`.OnEnding due to {Event}", Id, Event.Event);
        return Task.FromResult(StoreAndResume(LegacyFlowSteps.OnEnd));
    }

    protected Task<LegacyFlowTransition> OnEnd(CancellationToken cancellationToken)
    {
        Event.MarkHandled();
        if (Event.Event is not LegacyFlowResumeEvent)
            Log.LogInformation("`{Id}`.OnEnd: ignoring {Event}", Id, Event.Event);

        var transition = HardResumeAt != InfiniteHardResumeAt
            ? WaitForEvent(LegacyFlowSteps.OnEnd, InfiniteHardResumeAt)
            : default;
        return Task.FromResult(transition);
    }

    protected virtual Task<LegacyFlowTransition> OnMissingStep(CancellationToken cancellationToken)
        => throw Errors.NoStepImplementation(GetType(), Step);

    protected virtual Task<LegacyFlowTransition> HandleError(Exception error, CancellationToken cancellationToken)
        => Task.FromResult(LegacyFlowTransition.None);

    // Transition helpers

    protected LegacyFlowTransition WaitForEvent(Symbol nextStep, TimeSpan hardResumeDelay, string? tag = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(hardResumeDelay, TimeSpan.Zero);
        Event.MarkHandled();

        var hardResumeAt = Clocks.SystemClock.Now + hardResumeDelay;
        return new(this, nextStep, tag, hardResumeAt) { MustStore = true };
    }

    protected LegacyFlowTransition WaitForEvent(Symbol nextStep, string? tag = null)
        => WaitForEvent(nextStep, InfiniteHardResumeAt, tag);

    protected LegacyFlowTransition WaitForEvent(Symbol nextStep, Moment hardResumeAt, string? tag = null)
    {
        Event.MarkHandled();
        return new LegacyFlowTransition(this, nextStep, tag, hardResumeAt) { MustStore = true };
    }

    protected LegacyFlowTransition WaitForTimer(Symbol nextStep, TimeSpan delay, string? tag = null)
        => WaitForTimer(nextStep, delay, DefaultTimerDelayQuanta, tag);
    protected LegacyFlowTransition WaitForTimer(Symbol nextStep, TimeSpan delay, TimeSpan delayQuanta, string? tag = null)
    {
        Event.MarkHandled();
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var timerEvent = CreateTimerOperationEvent(delay, delayQuanta, tag);
        return new(this, nextStep, tag, timerEvent.DelayUntil, timerEvent);
    }

    protected LegacyFlowTransition WaitForTimer(Symbol nextStep, Moment delayUntil, string? tag = null)
        => WaitForTimer(nextStep, delayUntil, DefaultTimerDelayQuanta, tag);
    protected LegacyFlowTransition WaitForTimer(Symbol nextStep, Moment delayUntil, TimeSpan delayQuanta, string? tag = null)
    {
        Event.MarkHandled();
        var now = Clocks.SystemClock.Now;
        var delay = delayUntil - now;
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var timerEvent = CreateTimerOperationEvent(delay, delayQuanta, tag);
        return new(this, nextStep, tag, timerEvent.DelayUntil, timerEvent);
    }

    protected LegacyFlowTransition QueueResume(Symbol nextStep, string? tag = null)
    {
        Event.MarkHandled();
        var resumeEvent = CreateOperationEvent(new LegacyFlowResumeEvent(Id, false, tag), skipIfAlreadyScheduled: true);
        return new LegacyFlowTransition(this, nextStep, tag, resumeEvent);
    }

    protected LegacyFlowTransition StoreAndResume(Symbol nextStep, string? tag = null)
    {
        Event.MarkHandled();
        return new LegacyFlowTransition(this, nextStep, tag) { MustStore = true };
    }

    protected LegacyFlowTransition Resume(Symbol nextStep, string? tag = null)
    {
        Event.MarkHandled();
        return new LegacyFlowTransition(this, nextStep, tag);
    }

    protected LegacyFlowTransition End(string? tag = null)
    {
        var nextStep = Step == LegacyFlowSteps.OnEnd
            ? LegacyFlowSteps.OnEnd
            : LegacyFlowSteps.OnEnding;
        return StoreAndResume(nextStep, tag);
    }

    // Other protected methods

    protected Task<LegacyFlowTransition> InvokeStep(Symbol step, CancellationToken cancellationToken)
    {
        var stepFunc = LegacyFlowSteps.Get(GetType(), step, true)!;
        var result = stepFunc.Invoke(this, cancellationToken);
        return result as Task<LegacyFlowTransition>
            ?? throw StandardError.Internal("Any flow step must return a Task<FlowTransition>.");
    }

    protected virtual async ValueTask ApplyTransition(
        LegacyFlowTransition transition, IFlowEvent @event, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(
            "`{Id}`: '{Step}' + {EventType} -> {Transition}",
            Id, Step, @event.GetType().GetName(), transition);
        if (transition.IsNone)
            return;

        Step = transition.Step;
        HardResumeAt = transition.HardResumeAt;
        if (!transition.EffectiveMustStore)
            return;

        // Always runs locally
        var storeCommand = new Flows_Store(Id, Version) {
            Flow = Clone(),
            Events = transition.Events.IsEmpty ? null : transition.Events.ToArray(),
        };
        var version = await Host.Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);
        ((IFlowImpl)this).Version = version;
    }

    // Private methods

    private LegacyFlowWorklet RequireWorklet()
    {
        if (_worklet == null)
            throw ActualLab.Internal.Errors.NotInitialized(nameof(Worklet));

        return _worklet;
    }

    private OperationEvent CreateTimerOperationEvent(Moment delayUntil, TimeSpan delayQuanta, string? tag = null)
        => CreateOperationEvent(new LegacyFlowTimerEvent(Id, tag), delayUntil, delayQuanta);

    private OperationEvent CreateTimerOperationEvent(TimeSpan delayBy, TimeSpan delayQuanta, string? tag = null)
        => CreateOperationEvent(new LegacyFlowTimerEvent(Id, tag), delayBy, delayQuanta);

    private OperationEvent CreateOperationEvent<TFlowEvent>(TFlowEvent flowEvent, Moment delayUntil, TimeSpan delayQuanta)
        where TFlowEvent : IFlowEvent
    {
        var isDelayQuantized = delayQuanta > TimeSpan.Zero;
        var uuid = isDelayQuantized
            ? $"{Id.Value}:{flowEvent.GetType().GetName()}"
            : OperationEvent.UuidGenerator.Next();
        var result = new OperationEvent(uuid, flowEvent) {
            LoggedAt = Clocks.SystemClock.Now,
            UuidConflictStrategy = isDelayQuantized ? KeyConflictStrategy.Skip : KeyConflictStrategy.Fail,
        };
        if (isDelayQuantized)
            result.SetDelayUntil(delayUntil, delayQuanta);
        else
            result.SetDelayUntil(delayUntil);
        return result;
    }

    private OperationEvent CreateOperationEvent<TFlowEvent>(TFlowEvent flowEvent, TimeSpan delayBy, TimeSpan delayQuanta)
        where TFlowEvent : IFlowEvent
    {
        var isDelayQuantized = delayQuanta > TimeSpan.Zero;
        var uuid = isDelayQuantized
            ? $"{Id.Value}:{flowEvent.GetType().GetName()}"
            : OperationEvent.UuidGenerator.Next();
        var result = new OperationEvent(uuid, flowEvent) {
            LoggedAt = Clocks.SystemClock.Now,
            UuidConflictStrategy = isDelayQuantized ? KeyConflictStrategy.Skip : KeyConflictStrategy.Fail,
        };
        if (isDelayQuantized)
            result.SetDelayBy(delayBy, delayQuanta);
        else
            result.SetDelayBy(delayBy);
        return result;
    }

    private OperationEvent CreateOperationEvent<TFlowEvent>(TFlowEvent flowEvent, bool skipIfAlreadyScheduled = false)
        where TFlowEvent : IFlowEvent
    {
        var uuid = skipIfAlreadyScheduled
            ? $"{Id.Value}:{flowEvent.GetType().GetName()}"
            : OperationEvent.UuidGenerator.Next();
        var result = new OperationEvent(uuid, flowEvent) {
            LoggedAt = Clocks.SystemClock.Now,
            UuidConflictStrategy = skipIfAlreadyScheduled ? KeyConflictStrategy.Skip : KeyConflictStrategy.Fail,
        };
        return result;
    }
}
