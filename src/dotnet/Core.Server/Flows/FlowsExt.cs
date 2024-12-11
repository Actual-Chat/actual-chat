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
        var services = flows.GetServices();
        var flowRegistry = services.GetRequiredService<FlowRegistry>();
        var flowId = flowRegistry.NewId<TFlow>(arguments);
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
        var flowRegistry = services.GetRequiredService<FlowRegistry>();
        var flowId = flowRegistry.NewId(flowType, arguments);
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
        var flowRegistry = services.GetRequiredService<FlowRegistry>();
        var queues = services.Queues();
        var clocks = services.Clocks();
        var log = services.LogFor<IFlows>();

        var flowId = flowRegistry.NewId(flowType, arguments);
        var flow = await flows.Get(flowId, cancellationToken).ConfigureAwait(false);
        if (flow is null) {
            log.LogInformation("`{Id}`.GetAndResume: unable to resume because the flow was not found", flowId);
            return null;
        }
        var maxLastRunAt = clocks.SystemClock.Now + maxLastRunIn + (delay ?? TimeSpan.Zero);
        var flowResumeEvent = new FlowResumeEvent(flowId,
            false,
            tag,
            maxLastRunAt,
            clocks.SystemClock.Now + delay);
        await queues.Enqueue(flowResumeEvent, cancellationToken)
            .ConfigureAwait(false);
        return flow;
    }
}
