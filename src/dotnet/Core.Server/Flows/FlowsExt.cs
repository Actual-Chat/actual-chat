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

    public static async Task<TFlow?> GetAndResume<TFlow>(
        this IFlows flows,
        string arguments,
        string tag,
        Moment? delayUntil = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => (TFlow?)await flows.GetAndSendEvent(typeof(TFlow),
                arguments,
                (id, now) => new FlowResumeEvent(id,
                    false,
                    tag,
                    null,
                    delayUntil),
                cancellationToken)
            .ConfigureAwait(false);

    public static Task<Flow?> GetAndResume(
        this IFlows flows,
        Type flowType,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
        => flows.GetAndSendEvent(flowType,
            arguments,
            (id, now) => new FlowResumeEvent(id,
                false,
                tag,
                now + maxLastRunIn,
                now + delay),
            cancellationToken);

    public static async Task<TFlow?> GetAndReset<TFlow>(
        this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => (TFlow?)await flows.GetAndReset(typeof(TFlow),
                arguments,
                maxLastRunIn,
                tag,
                cancellationToken)
            .ConfigureAwait(false);

    public static Task<Flow?> GetAndReset(
        this IFlows flows,
        Type flowType,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
        => flows.GetAndSendEvent(flowType,
            arguments,
            (id, now) => new FlowResetEvent(id,
                tag,
                now + maxLastRunIn),
            cancellationToken);

    private static async Task<Flow?> GetAndSendEvent(
        this IFlows flows,
        Type flowType,
        string arguments,
        Func<FlowId, Moment, IFlowEvent> eventFactory,
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
        var flowEvent = eventFactory(flowId, now);
        await queues.Enqueue(flowEvent, cancellationToken).ConfigureAwait(false);
        return flow;
    }

    public static Task<Flow> StartOrReset<TFlow>(
        this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
        => flows.StartOrReset(typeof(TFlow), arguments, maxLastRunIn, tag, cancellationToken);

    public static async Task<Flow> StartOrReset(
        this IFlows flows,
        Type flowType,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        Flow.RequireCorrectType(flowType);
        var services = flows.GetServices();
        var queues = services.Queues();
        var clocks = services.Clocks();

        var flowId = flows.GetFlowId(flowType, arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        if (flow == null)
            return await flows.GetOrStart(flowType, arguments, cancellationToken).ConfigureAwait(false);

        var now = clocks.SystemClock.Now;
        var resetEvent = new FlowResetEvent(flowId, tag, now + maxLastRunIn);
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
