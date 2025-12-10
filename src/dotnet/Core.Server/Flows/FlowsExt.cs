using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualChat.Time;
using ActualLab.Diagnostics;

namespace ActualChat.Flows;

#pragma warning disable CA2254 // The logging message template should not vary between calls

public static partial class FlowsExt
{
    // NewId

    public static FlowId NewId<TFlow>(this IFlows flows, params ReadOnlySpan<string> arguments)
        where TFlow : Flow
        => flows.NewId(typeof(TFlow), FlowId.CombineArguments(arguments));

    public static FlowId NewId<TFlow>(this IFlows flows, string arguments)
        where TFlow : Flow
        => flows.NewId(typeof(TFlow), arguments);

    public static FlowId NewId(this IFlows flows, Type flowType, params ReadOnlySpan<string> arguments)
        => flows.NewId(flowType, FlowId.CombineArguments(arguments));

    public static FlowId NewId(this IFlows flows, Type flowType, string arguments)
    {
        var flowRegistry = flows.GetServices().GetRequiredService<FlowRegistry>();
        var flowId = flowRegistry.NewId(flowType, arguments);
        return flowId;
    }

    // TryGet - must be used mainly in tests

    // [ComputeMethod] - behaves exactly like a compute method
    public static async ValueTask<TFlow?> TryGet<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.NewId<TFlow>(arguments);
        var flowData = await flows.TryGetData(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flowData?.Flow; // Notice it doesn't check for deserialization errors here!
    }

    // Get

    // [ComputeMethod] - behaves exactly like a compute method
    public static ValueTask<TFlow> Get<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.Get<TFlow>(arguments, addDependency: true, cancellationToken);

    // [ComputeMethod] - behaves exactly like a compute method
    public static ValueTask<Flow> Get(this IFlows flows,
        FlowId flowId,
        CancellationToken cancellationToken = default)
        => flows.Get(flowId, addDependency: true, cancellationToken);

    // [ComputeMethod] - behaves exactly like a compute method when addDependency=true
    public static async ValueTask<TFlow> Get<TFlow>(this IFlows flows,
        string arguments,
        bool addDependency,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.NewId<TFlow>(arguments);
        var flow = await flows.Get(flowId, addDependency, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    // [ComputeMethod] - behaves exactly like a compute method when addDependency=true
    public static async ValueTask<Flow> Get(this IFlows flows,
        FlowId flowId,
        bool addDependency,
        CancellationToken cancellationToken = default)
    {
        var cFlowData = await Computed
            .Capture(() => flows.TryGetData(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var flowData = cFlowData.Value;
        if (flowData is not null) {
            if (flowData.DeserializationError is null) {
                if (addDependency)
                    _ = cFlowData.UseUntyped(allowInconsistent: true, cancellationToken);
                return flowData.Flow;
            }

            var log = flows.GetServices().LogFor<IFlows>();
            log.LogError(flowData.DeserializationError,
                "`{FlowId}`: deserialization failed for version {Version}, trying to restart it",
                flowData.Id, flowData.Version);
        }

        var expectedVersion = flowData?.Version ?? 0L;
        flowData = await flows.Start(flowId, expectedVersion, cancellationToken).ConfigureAwait(false);
        using (Computed.BeginIsolation()) // Just in case
            cFlowData = await cFlowData
                // ReSharper disable once AccessToModifiedClosure
                .When(x => x is not null && x.Version >= flowData.Version, cancellationToken)
                .ConfigureAwait(false);

        flowData = cFlowData.Value!;
        if (addDependency)
            _ = cFlowData.UseUntyped(allowInconsistent: true, cancellationToken);
        return flowData.Flow;
    }

    // Notify

    public static Task Notify<TFlow>(this IFlows flows,
        string arguments,
        IFlowEvent @event,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.Notify(flows.NewId<TFlow>(arguments), @event, ensureStarted: true, cancellationToken);

    public static Task Notify<TFlow>(this IFlows flows,
        string arguments,
        IFlowEvent @event,
        bool ensureStarted,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.Notify(flows.NewId<TFlow>(arguments), @event, ensureStarted, cancellationToken);

    public static Task Notify(
        this IFlows flows,
        FlowId flowId,
        IFlowEvent @event,
        CancellationToken cancellationToken = default)
        => flows.Notify(flowId, @event, ensureStarted: true, cancellationToken);

    public static async Task Notify(this IFlows flows,
        FlowId flowId,
        IFlowEvent @event,
        bool ensureStarted,
        CancellationToken cancellationToken = default)
    {
        var services = flows.GetServices();
        var queues = services.Queues();
        if (ensureStarted)
            await flows.Get(flowId, addDependency: false, cancellationToken).ConfigureAwait(false);
        await queues.Enqueue(@event, cancellationToken).ConfigureAwait(false);

        var log = services.LogFor<IFlows>();
        var debugLog = log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Flows);
        if (debugLog != null) {
            var delayUntil = (@event as IHasDelayUntil)?.DelayUntil;
            var maxLastRunAt = (@event as LegacyFlowResumeEvent)?.MaxLastRunAt;
            debugLog.LogDebug(
                "`{Id}`.Notify: sent {Event} with DelayUntil={DelayUntil} and MaxLastRunAt={MaxLastRunAt}",
                flowId,
                @event,
                delayUntil,
                maxLastRunAt);
        }
    }
}
