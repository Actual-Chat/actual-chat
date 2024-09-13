using ActualChat.Flows.Infrastructure;

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
        var services = flows.GetServices();
        var flowRegistry = services.GetRequiredService<FlowRegistry>();
        var flowId = flowRegistry.NewId<TFlow>(arguments);
        var flow = await flows.GetOrStart(flowId, cancellationToken).ConfigureAwait(false);
        return (TFlow)flow;
    }
}
