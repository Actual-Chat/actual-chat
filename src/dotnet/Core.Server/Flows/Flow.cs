using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IHasId<FlowId>, IFlowImpl
{
    public static class Defaults
    {
        public static TimeSpan KeepAliveFor { get; } = TimeSpan.FromSeconds(10);
        public static RetryDelaySeq FailureDelays { get; } = RetryDelaySeq.Exp(0.5, 3);
    }

    public static Moment InfiniteHardResumeAt { get; } = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly bool DebugMode = Constants.DebugMode.Flows;
    private FlowWorklet? _worklet;
    private ILogger? _log;

    // Persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol Step { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Moment? HardResumeAt { get; private set; }

    // Used by FlowWorklet
    [IgnoreDataMember, MemoryPackIgnore]
    public TimeSpan KeepAliveFor { get; set; } = Defaults.KeepAliveFor;
    [IgnoreDataMember, MemoryPackIgnore]
    public RetryDelaySeq FailureDelays { get; set; } = Defaults.FailureDelays;

    protected FlowHost Host => Worklet.Host;
    protected FlowWorklet Worklet => RequireWorklet();
    protected FlowEventBin Event { get; private set; } = null!;
    protected MomentClockSet Clocks => Host.Clocks;
    protected ILogger Log => _log ??= Host.Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, DebugMode);

    public void Initialize(FlowId id, long version, Symbol step, Moment? hardResumeAt = null, FlowWorklet? worklet = null)
    {
        Id = id;
        Version = version;
        Step = step;
        HardResumeAt = hardResumeAt;
        _worklet = worklet;
        if (worklet != null)
            OnInitialized();
    }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}' @ {Step}, v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    public virtual async Task<FlowTransition> ProcessEvent(IFlowEvent evt, CancellationToken cancellationToken)
    {
        Event = new FlowEventBin(this, evt);
        var step = Step;
        FlowTransition transition;
        try {
            if (Event.Is<IFlowControlEvent>(out var flowControlEvent)) {
                step = flowControlEvent.GetNextStep(this);
                if (step.IsEmpty)
                    return default;
            }
            transition = await InvokeStep(step, cancellationToken).ConfigureAwait(false);

            if (!Event.IsHandled) {
                var error = Errors.UnhandledEvent(GetType(), Step, evt.GetType());
                Log.LogError(error,
                    "`{Id}`.HandleEvent @ '{Step}': unhandled event '{EventType}'",
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

    // Default steps

    protected virtual void OnInitialized()
    { }

    protected abstract Task<FlowTransition> OnReset(CancellationToken cancellationToken);

    protected virtual Task<FlowTransition> OnHardResume(CancellationToken cancellationToken)
        => InvokeStep(Step, cancellationToken);

    protected Task<FlowTransition> OnEnding(CancellationToken cancellationToken)
    {
        Event.MarkHandled();
        Log.LogInformation("`{Id}`.OnEnding due to {Event}", Id, Event.Event);
        return Task.FromResult(StoreAndResume(FlowSteps.OnEnd));
    }

    protected Task<FlowTransition> OnEnd(CancellationToken cancellationToken)
    {
        Event.MarkHandled();
        if (Event.Event is not FlowResumeEvent)
            Log.LogInformation("`{Id}`.OnEnd: ignoring {Event}", Id, Event.Event);

        var transition = HardResumeAt != InfiniteHardResumeAt
            ? WaitForEvent(FlowSteps.OnEnd, InfiniteHardResumeAt)
            : default;
        return Task.FromResult(transition);
    }

    protected virtual Task<FlowTransition> OnMissingStep(CancellationToken cancellationToken)
        => throw Errors.NoStepImplementation(GetType(), Step);

    protected virtual Task<FlowTransition> HandleError(Exception error, CancellationToken cancellationToken)
        => Task.FromResult(FlowTransition.None);

    // Transition helpers

    protected FlowTransition WaitForEvent(Symbol nextStep, TimeSpan hardResumeDelay, string? tag = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(hardResumeDelay, TimeSpan.Zero);

        var hardResumeAt = Clocks.SystemClock.Now + hardResumeDelay;
        return new(this, nextStep, tag, hardResumeAt) { MustStore = true };
    }

    protected FlowTransition WaitForEvent(Symbol nextStep, Moment hardResumeAt, string? tag = null)
        => new(this, nextStep, tag, hardResumeAt) { MustStore = true };

    protected FlowTransition WaitForTimer(Symbol nextStep, TimeSpan delay, string? tag = null)
    {
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var resumeAt = Clocks.SystemClock.Now + delay;
        var timerEvent = new OperationEvent(resumeAt, new FlowTimerEvent(Id, tag));
        return new(this, nextStep, tag, resumeAt, timerEvent);
    }

    protected FlowTransition WaitForTimer(Symbol nextStep, Moment resumeAt, string? tag = null)
    {
        var now = Clocks.SystemClock.Now;
        var delay = resumeAt - now;
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var timerEvent = new OperationEvent(resumeAt, new FlowTimerEvent(Id, tag));
        return new(this, nextStep, tag, resumeAt, timerEvent);
    }

    protected FlowTransition StoreAndResume(Symbol nextStep, string? tag = null)
        => new(this, nextStep, tag) { MustStore = true };

    protected FlowTransition Resume(Symbol nextStep, string? tag = null)
        => new(this, nextStep, tag);

    protected FlowTransition End(string? tag = null)
    {
        var nextStep = Step == FlowSteps.OnEnd
            ? FlowSteps.OnEnd
            : FlowSteps.OnEnding;
        return StoreAndResume(nextStep, tag);
    }

    // Other protected methods

    protected Task<FlowTransition> InvokeStep(Symbol step, CancellationToken cancellationToken)
    {
        var stepFunc = FlowSteps.Get(GetType(), step, true)!;
        var result = stepFunc.Invoke(this, cancellationToken);
        return result as Task<FlowTransition>
            ?? throw StandardError.Internal("Any flow step must return a Task<FlowTransition>.");
    }

    protected virtual async ValueTask ApplyTransition(
        FlowTransition transition, IFlowEvent @event, CancellationToken cancellationToken)
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

        var storeCommand = new Flows_Store(Id, Version) {
            Flow = Clone(),
            AddEvents = transition.Events.IsEmpty ? null : transition.Events.ToArray(),
        };
        Version = await Host.Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);
    }

    // IFlowImpl

    FlowHost IFlowImpl.Host => Worklet.Host;
    FlowWorklet IFlowImpl.Worklet => Worklet;
    FlowEventBin IFlowImpl.Event => Event;

    // Other helpers

    public static void RequireCorrectType(Type flowType)
    {
        if (!typeof(Flow).IsAssignableFrom(flowType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo<Flow>(flowType);
    }

    private FlowWorklet RequireWorklet()
    {
        if (_worklet == null)
            throw ActualLab.Internal.Errors.NotInitialized(nameof(Worklet));

        return _worklet;
    }
}
