using ActualChat.Flows.Infrastructure;
using ActualChat.Queues;

namespace ActualChat.Flows;

public static class FlowsExt
{
    public static async Task<TFlow?> Get<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flowId = flows.GetFlowId(typeof(TFlow), arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow?)flow;
    }

    public static async Task<TFlow> GetOrStart<TFlow>(this IFlows flows,
        string arguments,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
    {
        var flow = await flows.GetOrStart(typeof(TFlow), arguments, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }

    public static async Task<Flow> GetOrStart(this IFlows flows,
        Type flowType,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        Flow.RequireCorrectType(flowType);
        var services = flows.GetServices();
        var flowId = flows.GetFlowId(flowType, arguments);
        return await flows.GetOrStart(flowId, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<TFlow?> GetAndResume<TFlow>(
        this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => (TFlow?)await flows.GetAndResume(typeof(TFlow),
                arguments,
                maxLastRunIn,
                tag,
                delay,
                cancellationToken)
            .ConfigureAwait(false);

    public static async Task<Flow?> GetAndResume(
        this IFlows flows,
        Type flowType,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        Flow.RequireCorrectType(flowType);
        var services = flows.GetServices();
        var queues = services.Queues();
        var clocks = services.Clocks();
        var log = services.LogFor<IFlows>();

        var flowId = flows.GetFlowId(flowType, arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        if (flow is null) {
            log.LogInformation("`{Id}`.GetAndResume: skip resume because the flow was not found", flowId);
            return null;
        }
        var now = clocks.SystemClock.Now;
        var flowResumeEvent = new FlowResumeEvent(flowId,
            false,
            tag,
            now + maxLastRunIn,
            now + delay);
        await queues.Enqueue(flowResumeEvent, cancellationToken).ConfigureAwait(false);
        return flow;
    }

    public static Task<Flow> StartOrReset<TFlow>(
        this IFlows flows,
        string arguments,
        string? tag = null,
        CancellationToken cancellationToken = default)
        => flows.StartOrReset(typeof(TFlow), arguments, tag, cancellationToken);

    public static async Task<Flow> StartOrReset(
        this IFlows flows,
        Type flowType,
        string arguments,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        Flow.RequireCorrectType(flowType);
        var services = flows.GetServices();
        var queues = services.Queues();

        var flowId = flows.GetFlowId(flowType, arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        if (flow == null)
            return await flows.GetOrStart(flowType, arguments, cancellationToken).ConfigureAwait(false);

        var resetEvent = new FlowResetEvent(flowId, tag);
        await queues.Enqueue(resetEvent, cancellationToken).ConfigureAwait(false);
        return flow;
    }

    private static FlowId GetFlowId(this IFlows flows, Type flowType, string arguments)
    {
        var services = flows.GetServices();
        var flowRegistry = services.GetRequiredService<FlowRegistry>();
        var flowId = flowRegistry.NewId(flowType, arguments);
        return flowId;
    }
}
