using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IHasId<FlowId>, IFlowImpl
{
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

    protected FlowHost Host => Worklet.Host;
    protected FlowWorklet Worklet => RequireWorklet();
    protected FlowEventBin Event { get; private set; } = null!;
    protected MomentClockSet Clocks => Host.Clocks;
    protected ILogger Log => _log ??= Host.Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, DebugMode);

    public void Initialize(FlowId id, long version, Moment? resumesAt, Symbol step, FlowWorklet? worklet = null)
    {
        Id = id;
        Version = version;
        HardResumeAt = resumesAt;
        Step = step;
        _worklet = worklet;
    }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}' @ {Step}, v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    public virtual async Task<FlowTransition> HandleEvent(IFlowEvent evt, CancellationToken cancellationToken)
    {
        Event = new FlowEventBin(this, evt);
        var step = Step;
        FlowTransition transition;
        try {
            if (Event.Is<IFlowSystemEvent>(out var systemEvent)) {
                // Overriding current step for some IFlowSystemEvent-s
                if (systemEvent is FlowKillEvent)
                    step = FlowSteps.OnKill;
                else if (systemEvent is FlowResetEvent || step.IsEmpty)
                    step = FlowSteps.OnReset;
                else if (systemEvent is FlowHardResumeEvent)
                    step = FlowSteps.OnHardResume;
                // There is also FlowResumeEvent, which doesn't require step change
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
            Step = step;
            Event = new FlowEventBin(this, evt);
            transition = await OnError(ex, cancellationToken).ConfigureAwait(false);
            if (!Event.IsHandled)
                throw;
        }
        finally {
            Event = null!;
        }
        await ApplyTransition(transition, evt, cancellationToken).ConfigureAwait(false);
        return transition;
    }

    // Default options

    public virtual FlowOptions GetOptions()
        => FlowOptions.Default;

    // Default steps

    protected abstract Task<FlowTransition> OnReset(CancellationToken cancellationToken);

    protected virtual Task<FlowTransition> OnHardResume(CancellationToken cancellationToken)
        => InvokeStep(Step, cancellationToken);

    protected virtual Task<FlowTransition> OnKill(CancellationToken cancellationToken)
        => Task.FromResult(End(true));

    protected virtual Task<FlowTransition> OnDelayedEnd(CancellationToken cancellationToken)
    {
        var removeDelay = GetOptions().RemoveDelay;
        return Task.FromResult(WaitForTimer(FlowSteps.OnEnd, removeDelay));
    }

    protected Task<FlowTransition> OnEnd(CancellationToken cancellationToken)
    {
        if (!Event.IsHandled)
            Event.Require<FlowTimerEvent>();
        return Task.FromResult(StoreAndResume(FlowSteps.Removed));
    }

    protected virtual Task<FlowTransition> OnMissingStep(CancellationToken cancellationToken)
        => throw Errors.NoStepImplementation(GetType(), Step);

    protected virtual Task<FlowTransition> OnError(Exception error, CancellationToken cancellationToken)
        => Task.FromResult(default(FlowTransition));

    // Transition helpers

    protected FlowTransition WaitForEvent(Symbol nextStep, TimeSpan hardResumeDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(hardResumeDelay, TimeSpan.Zero);

        var hardResumeAt = Clocks.SystemClock.Now + hardResumeDelay;
        return new(this, nextStep, hardResumeAt) { MustStore = true };
    }

    protected FlowTransition WaitForEvent(Symbol nextStep, Moment hardResumeAt)
        => new(this, nextStep, hardResumeAt) { MustStore = true };

    protected FlowTransition WaitForTimer(Symbol nextStep, TimeSpan delay, string? tag = null)
    {
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var resumeAt = Clocks.SystemClock.Now + delay;
        var timerEvent = new OperationEvent(resumeAt, new FlowTimerEvent(Id, tag));
        return new(this, nextStep, resumeAt, timerEvent);
    }

    protected FlowTransition WaitForTimer(Symbol nextStep, Moment resumeAt, string? tag = null)
    {
        var now = Clocks.SystemClock.Now;
        var delay = resumeAt - now;
        if (delay <= TimeSpan.Zero)
            return StoreAndResume(nextStep);

        var timerEvent = new OperationEvent(resumeAt, new FlowTimerEvent(Id, tag));
        return new(this, nextStep, resumeAt, timerEvent);
    }

    protected FlowTransition StoreAndResume(Symbol nextStep)
        => new(this, nextStep) { MustStore = true };

    protected FlowTransition Resume(Symbol nextStep)
        => new(this, nextStep);

    protected FlowTransition End(bool instantly = false)
    {
        var nextStep = instantly ? FlowSteps.OnEnd : FlowSteps.OnDelayedEnd;
        return StoreAndResume(nextStep);
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
