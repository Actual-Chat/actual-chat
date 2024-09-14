using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
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
    public bool CanResume { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol Step { get; private set; }

    protected FlowHost Host => Worklet.Host;
    protected FlowWorklet Worklet => RequireWorklet();
    protected FlowEventBin Event { get; private set; } = null!;
    protected MomentClockSet Clocks => Host.Clocks;
    protected ILogger Log => _log ??= Host.Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, DebugMode);

    public void Initialize(FlowId id, long version, bool canResume, Symbol step, FlowWorklet? worklet = null)
    {
        Id = id;
        Version = version;
        CanResume = canResume;
        Step = step;
        _worklet = worklet;
    }

    public override string ToString()
        => $"{GetType().Name}('{Id.Value}' @ {Step}, v.{Version.FormatVersion()})";

    public virtual Flow Clone()
        => MemberwiseCloner.Invoke(this);

    public virtual async Task<FlowTransition> HandleEvent(IFlowEvent evt, CancellationToken cancellationToken)
    {
        RequireWorklet();
        Event = new FlowEventBin(this, evt);
        var step = Step;
        FlowTransition transition;
        try {
            transition = await InvokeStep(step, true, cancellationToken).ConfigureAwait(false);
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

    protected Func<Flow, CancellationToken, Task<FlowTransition>>? GetStepFunc(Symbol step)
        => FlowSteps.Get(GetType(), step);

    protected Task<FlowTransition> InvokeStep(Symbol step, bool useOnMissingStep, CancellationToken cancellationToken)
    {
        if (Event.Is<ISystemFlowEvent>(out var systemFlowEvent)) {
            if (systemFlowEvent is FlowKillEvent)
                step = FlowSteps.OnKill;
            else if (systemFlowEvent is FlowResetEvent || step.IsEmpty)
                step = FlowSteps.OnReset;
            else if (systemFlowEvent is FlowResumeEvent { IsExternal: true })
                step = FlowSteps.OnExternalResume;
        }

        var stepFunc = GetStepFunc(step);
        if (useOnMissingStep)
            stepFunc ??= static (flow, ct) => flow.OnMissingStep(ct);
        if (stepFunc == null)
            throw Errors.NoStepImplementation(GetType(), step.Value);

        return stepFunc.Invoke(this, cancellationToken);
    }

    protected abstract Task<FlowTransition> OnReset(CancellationToken cancellationToken);

    protected virtual Task<FlowTransition> OnExternalResume(CancellationToken cancellationToken)
        => Task.FromResult(Wait(Step, false)); // "Do nothing"

    protected virtual Task<FlowTransition> OnKill(CancellationToken cancellationToken)
        => Task.FromResult(GotoEnding());

    protected virtual Task<FlowTransition> OnEnding(CancellationToken cancellationToken)
    {
        var removeDelay = GetOptions().RemoveDelay;
        return Task.FromResult(removeDelay <= TimeSpan.Zero
            ? Goto(FlowSteps.OnEnded)
            : Wait(FlowSteps.OnEnded).AddTimerEvent(removeDelay));
    }

    protected Task<FlowTransition> OnEnded(CancellationToken cancellationToken)
    {
        if (!Event.IsHandled)
            Event.Require<FlowTimerEvent>();
        return Task.FromResult(Goto(FlowSteps.Removed, true));
    }

    protected virtual Task<FlowTransition> OnMissingStep(CancellationToken cancellationToken)
        => throw Errors.NoStepImplementation(GetType(), Step);

    protected virtual Task<FlowTransition> OnError(Exception error, CancellationToken cancellationToken)
        => Task.FromResult(default(FlowTransition));

    // Transition helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected FlowTransition Wait(Symbol step, bool mustStore = true)
        => new(this, step) { MustStore = mustStore };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected FlowTransition Goto(Symbol step, bool mustStore = false)
        => new(this, step) { MustStore = mustStore, MustResume = true };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected FlowTransition GotoEnding()
        => Goto(nameof(OnEnding));

    protected virtual async ValueTask ApplyTransition(
        FlowTransition transition, IFlowEvent @event, CancellationToken cancellationToken)
    {
        var worklet = RequireWorklet();
        DebugLog?.LogDebug(
            "`{Id}`: '{Step}' + {EventType} -> {Transition}",
            Id, Step, @event.GetType().GetName(), transition);

        Step = transition.Step;
        if (!transition.EffectiveMustStore)
            return;

        var storeCommand = new Flows_Store(Id, Version) {
            Flow = Clone(),
            AddEvents = transition.Events.IsEmpty ? null : transition.Events.ToArray(),
        };
        Version = await worklet.Host.Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);
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
