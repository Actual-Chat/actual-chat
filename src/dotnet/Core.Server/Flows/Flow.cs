using ActualChat.Flows.Infrastructure;
using ActualChat.Flows.Internal;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

public abstract class Flow : IHasId<FlowId>, IFlowImpl
{
    private FlowWorklet? _worklet;
    private ILogger? _log;

    // Persisted to the DB directly
    [IgnoreDataMember, MemoryPackIgnore]
    public FlowId Id { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public long Version { get; private set; }
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol Step { get; private set; }

    // IWorkerFlow properties (shouldn't be persisted)
    protected FlowHost Host => Worklet.Host;
    protected FlowWorklet Worklet => RequireWorklet();
    protected FlowEventBin Event { get; private set; } = null!;
    protected MomentClockSet Clocks => Host.Clocks;
    protected ILogger Log => _log ??= Host.Services.LogFor(GetType());

    public void Initialize(FlowId id, long version, Symbol step = default, FlowWorklet? worklet = null)
    {
        Id = id;
        Version = version;
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
            transition = Event.Is<ISystemFlowEvent>(out var systemFlowEvent)
                ? await OnSystemEvent(systemFlowEvent, cancellationToken).ConfigureAwait(false)
                : await InvokeStep(step, true, cancellationToken).ConfigureAwait(false);
            if (!Event.IsUsed)
                Worklet.Log.LogWarning(
                    "Flow {FlowType} ignored event {Event} on step '{Step}'",
                    GetType().Name, evt, step);
        }
        catch (Exception ex) when (!ex.IsCancellationOf(cancellationToken)) {
            Step = step;
            Event = new FlowEventBin(this, evt);
            transition = await OnError(ex, cancellationToken).ConfigureAwait(false);
            if (!Event.IsUsed)
                throw;
        }
        finally {
            Event = null!;
        }
        await ApplyTransition(transition, cancellationToken).ConfigureAwait(false);
        return transition;
    }

    protected virtual Task<FlowTransition> OnSystemEvent(ISystemFlowEvent evt, CancellationToken cancellationToken)
    {
        Event.MarkUsed();
        return evt switch {
            FlowStartEvent => Step.IsEmpty ? OnStart(cancellationToken) : Task.FromResult(Wait(Step, false)),
            FlowResetEvent => OnReset(cancellationToken),
            FlowKillEvent => OnKill(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(evt)),
        };
    }

    // Default options

    public virtual FlowOptions GetOptions()
        => FlowOptions.Default;

    // Default steps

    protected Func<Flow, CancellationToken, Task<FlowTransition>>? GetStepFunc(Symbol step)
        => FlowSteps.Get(GetType(), step);

    protected Task<FlowTransition> InvokeStep(Symbol step, bool useOnMissingStep, CancellationToken cancellationToken)
    {
        var stepFunc = GetStepFunc(step);
        if (useOnMissingStep)
            stepFunc ??= static (flow, ct) => flow.OnMissingStep(ct);
        if (stepFunc == null)
            throw Errors.NoStepImplementation(GetType(), step.Value);

        return stepFunc.Invoke(this, cancellationToken);
    }

    protected abstract Task<FlowTransition> OnStart(CancellationToken cancellationToken);

    protected virtual Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        var (id, version, step, worklet) = (Id, Version, Step, Worklet);

        // Copy every field from a new flow of the same type
        var type = GetType();
        var newFlow = type.CreateInstance();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            field.SetValue(this, field.GetValue(newFlow));

        // Initialize & immediately jump to OnStart; Goto(...) isn't really necessary here
        Initialize(id, version, step, worklet);
        return OnStart(cancellationToken);
    }

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
        => Task.FromResult(Goto(FlowSteps.OnEnded));

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
        => new(this, step) { MustStore = mustStore, MustWait = false };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected FlowTransition GotoEnding()
        => Goto(nameof(OnEnding));

    protected virtual async ValueTask ApplyTransition(FlowTransition transition, CancellationToken cancellationToken)
    {
        RequireWorklet();
        Step = transition.Step;
        if (!transition.EffectiveMustStore)
            return;

        var storeCommand = new Flows_Store(Id, Version) {
            Flow = Clone(),
            AddEvents = transition.Events.IsEmpty ? null : transition.Events.ToArray(),
        };
        Version = await Worklet.Host.Commander.Call(storeCommand, cancellationToken).ConfigureAwait(false);
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
