using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;
using ActualChat.Time;
using ActualLab.CommandR.Operations;
using ActualLab.Diagnostics;

namespace ActualChat.Flows;

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

    // TryGet

    // [ComputeMethod] - behaves exactly like a compute method
    public static async ValueTask<TFlow?> TryGet<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.NewId<TFlow>(arguments);
        var flow = await flows.TryGet(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flow;
    }

    // Get

    // [ComputeMethod] - behaves exactly like a compute method
    public static async ValueTask<TFlow> Get<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.NewId<TFlow>(arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    // [ComputeMethod] - behaves exactly like a compute method
    public static async ValueTask<Flow> Get(this IFlows flows,
        FlowId flowId,
        CancellationToken cancellationToken = default)
    {
        var cFlow = await Computed
            .Capture(() => flows.TryGet(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (cFlow.Value is not null)
            goto exit;

        using (Computed.BeginIsolation()) {
            await flows.Start(flowId, cancellationToken).ConfigureAwait(false);
            // Await for the new flow to be visible via TryGet in the current process
            cFlow = await cFlow.When(x => x is not null, cancellationToken).ConfigureAwait(false);
        }

        exit:
        // Register a dependency
        await cFlow.UseUntyped(allowInconsistent: true, cancellationToken).ConfigureAwait(false);
        return cFlow.Value!;
    }

    // EnsureStarted - like Get, but w/o registering a dependency

    public static async ValueTask<TFlow> EnsureStarted<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.NewId<TFlow>(arguments);
        var flow = await flows.EnsureStarted(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    public static async ValueTask<Flow> EnsureStarted(this IFlows flows,
        FlowId flowId,
        CancellationToken cancellationToken = default)
    {
        var cFlow = await Computed
            .Capture(() => flows.TryGet(flowId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (cFlow.Value is { } flow)
            return flow;

        using (Computed.BeginIsolation()) {
            await flows.Start(flowId, cancellationToken).ConfigureAwait(false);
            // Await for the new flow to be visible via TryGet in the current process
            cFlow = await cFlow.When(x => x is not null, cancellationToken).ConfigureAwait(false);
            return cFlow.Value!;
        }
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
            await flows.EnsureStarted(flowId, cancellationToken).ConfigureAwait(false);
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
