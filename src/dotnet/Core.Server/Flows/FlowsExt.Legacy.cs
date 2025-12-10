namespace ActualChat.Flows;

public static partial class FlowsExt
{
    // LegacyResume

    public static Task LegacyResume<TFlow>(this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.LegacyResume(flows.NewId<TFlow>(arguments), maxLastRunIn, tag, delay, cancellationToken);

    public static async Task LegacyResume(this IFlows flows,
        FlowId flowId,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        var services = flows.GetServices();
        await flows.Get(flowId, addDependency: false, cancellationToken).ConfigureAwait(false);

        var now = services.Clocks().SystemClock.Now;
        var delayUntil = (now + delay) ?? default;
        var @event = new LegacyFlowResumeEvent(flowId, IsHardResume: false, tag, now + maxLastRunIn, delayUntil);
        await flows.Notify(flowId, @event, ensureStarted: false, cancellationToken).ConfigureAwait(false);
    }

    // LegacyResume

    public static Task LegacyReset<TFlow>(
        this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.LegacyReset(flows.NewId<TFlow>(arguments), maxLastRunIn, tag, cancellationToken);

    public static async Task LegacyReset(this IFlows flows,
        FlowId flowId,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var services = flows.GetServices();
        await flows.Get(flowId, addDependency: false, cancellationToken).ConfigureAwait(false);

        var now = services.Clocks().SystemClock.Now;
        var @event = new LegacyFlowResetEvent(flowId, tag, now + maxLastRunIn);
        await flows.Notify(flowId, @event, ensureStarted: false, cancellationToken).ConfigureAwait(false);
    }
}
