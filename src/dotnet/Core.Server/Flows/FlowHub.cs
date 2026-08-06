using ActualChat.Diagnostics;
using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualLab.Diagnostics;

namespace ActualChat.Flows;

public sealed class FlowHub(IServiceProvider services) : IHasServices
{
    internal ILogger Log => field ??= Services.LogFor(GetType());
    internal ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);

    public IServiceProvider Services { get; } = services;
    public HostInfo HostInfo { get; } = services.HostInfo();
    public FlowDefs Defs { get; } = services.GetRequiredService<FlowDefs>();
    public IFlowBackend Backend => field ??= Services.GetRequiredService<IFlowBackend>();
    public ICommander Commander { get; } = services.Commander();
    public IQueues Queues { get; } = services.Queues();
    public MomentClockSet Clocks { get; } = services.Clocks();
    public Moment SystemNow => Clocks.SystemClock.Now;

    // NewId

    public FlowId NewId<TFlow>(string arguments)
        where TFlow : Flow
        => new (Defs.ByType[typeof(TFlow)].Name, arguments);

    public FlowId NewId<TFlow>(params ReadOnlySpan<string> arguments)
        where TFlow : Flow
        => new (Defs.ByType[typeof(TFlow)].Name, FlowId.CombineArguments(arguments));

    public FlowId NewId(Type flowType, string arguments)
        => new (Defs.ByType[flowType].Name, arguments);

    public FlowId NewId(Type flowType, params ReadOnlySpan<string> arguments)
        => new (Defs.ByType[flowType].Name, FlowId.CombineArguments(arguments));

    // NewResumeEvent

    public FlowResumeEvent NewResumeEvent(FlowId flowId)
        => new(flowId, this);

    public FlowResumeEvent NewResumeEvent<TFlow>(string arguments)
        where TFlow : Flow
        => new(NewId<TFlow>(arguments), this);

    public FlowResumeEvent NewResumeEvent<TFlow>(params ReadOnlySpan<string> arguments)
        where TFlow : Flow
        => new(NewId<TFlow>(FlowId.CombineArguments(arguments)), this);

    public FlowResumeEvent NewResumeEvent(Type flowType, string arguments)
        => new(NewId(flowType, arguments), this);

    public FlowResumeEvent NewResumeEvent(Type flowType, params ReadOnlySpan<string> arguments)
        => new(NewId(flowType, FlowId.CombineArguments(arguments)), this);

    // TryGet - must be used mainly in tests

    // [ComputeMethod] - behaves exactly like a compute method
    public async ValueTask<TFlow?> TryGet<TFlow>(string arguments, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = NewId<TFlow>(arguments);
        var flowData = await Backend.TryGetData(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flowData?.GetFlow(this);
    }

    // [ComputeMethod] - behaves exactly like a compute method
    public async ValueTask<TFlow?> TryGet<TFlow>(FlowId flowId, CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowData = await Backend.TryGetData(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flowData?.GetFlow(this);
    }

    // Get

    // [ComputeMethod] - behaves exactly like a compute method
    public ValueTask<TFlow> Get<TFlow>(
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => Get<TFlow>(arguments, addDependency: true, cancellationToken);

    // [ComputeMethod] - behaves exactly like a compute method
    public async ValueTask<TFlow> Get<TFlow>(
        FlowId flowId,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = await Get(flowId, addDependency: true, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    // [ComputeMethod] - behaves exactly like a compute method
    public ValueTask<Flow> Get(
        FlowId flowId,
        CancellationToken cancellationToken = default)
        => Get(flowId, addDependency: true, cancellationToken);

    // [ComputeMethod] - behaves exactly like a compute method when addDependency=true
    public async ValueTask<TFlow> Get<TFlow>(
        string arguments,
        bool addDependency,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = NewId<TFlow>(arguments);
        var flow = await Get(flowId, addDependency, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    // [ComputeMethod] - behaves exactly like a compute method when addDependency=true
    public async ValueTask<Flow> Get(
        FlowId flowId,
        bool addDependency,
        CancellationToken cancellationToken = default)
    {
        using var activity = CoreServerInstruments.ActivitySource.StartActivity(GetType(), activityKind: ActivityKind.Client);

        var cFlowData = await Computed
            .Capture(() => Backend.TryGetData(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var flowData = cFlowData.Value;
        if (flowData is not null) {
            if (addDependency)
                _ = cFlowData.UseUntyped(allowInconsistent: true, cancellationToken);
            return flowData.GetFlow(this);
        }

        var expectedVersion = flowData?.Version ?? 0L;
        flowData = await Backend.Start(flowId, expectedVersion, cancellationToken).ConfigureAwait(false);
        using (Computed.BeginIsolation()) // Just in case
            cFlowData = await cFlowData
                // ReSharper disable once AccessToModifiedClosure
                .When(x => x is not null && x.Version >= flowData.Version, cancellationToken)
                .ConfigureAwait(false);

        flowData = cFlowData.Value!;
        if (addDependency)
            _ = cFlowData.UseUntyped(allowInconsistent: true, cancellationToken);
        return flowData.GetFlow(this);
    }

    // Internal methods

    internal async Task Schedule(FlowResumeEvent resumeEvent, CancellationToken cancellationToken)
    {
        var delayUntil = resumeEvent.DelayUntil;
        var delay = (delayUntil - SystemNow).Positive();
        if (delay > TimeSpan.Zero) // This has to go through DbEvents
            await Commander.Call(new Flows_ScheduleResume(resumeEvent), cancellationToken).ConfigureAwait(false);
        else
            await Queues.Enqueue(resumeEvent, cancellationToken).ConfigureAwait(false);

        if (DebugLog is { } debugLog) {
            var flowId = resumeEvent.FlowId;
            var eventKind = resumeEvent.MustReset ? "restart" : "resume";
            var delayQuanta = resumeEvent.DelayQuanta;
            if (delayQuanta > TimeSpan.Zero)
                debugLog.LogDebug(
                    "`{FlowId}`.ScheduleResume: {EventKind} in {Delay} (at {DelayUntil}) mod {DelayQuanta}",
                    flowId, eventKind, delay.ToShortString(), delayUntil, delayQuanta.ToShortString());
            else
                debugLog.LogDebug(
                    "`{FlowId}`.ScheduleResume: {EventKind} in {Delay} (at {DelayUntil})",
                    flowId, eventKind, delay.ToShortString(), delayUntil);
        }
    }
}
