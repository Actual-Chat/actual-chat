namespace ActualChat.Flows;

public static partial class FlowsExt
{
    // Resume

    public static Task Resume<TFlow>(this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.Resume(flows.NewId<TFlow>(arguments), maxLastRunIn, tag, delay, cancellationToken);

    public static async Task Resume(this IFlows flows,
        FlowId flowId,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        TimeSpan? delay = null,
        CancellationToken cancellationToken = default)
    {
        var services = flows.GetServices();
        await flows.EnsureStarted(flowId, cancellationToken).ConfigureAwait(false);

        var now = services.Clocks().SystemClock.Now;
        var delayUntil = (now + delay) ?? default;
        var @event = new FlowResumeEvent(flowId, IsHardResume: false, tag, now + maxLastRunIn, delayUntil);
        await flows.Notify(flowId, @event, ensureStarted: false, cancellationToken).ConfigureAwait(false);
    }

    // Reset

    public static Task Reset<TFlow>(
        this IFlows flows,
        string arguments,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
        where TFlow : Flow
        => flows.Reset(flows.NewId<TFlow>(arguments), maxLastRunIn, tag, cancellationToken);

    public static async Task Reset(this IFlows flows,
        FlowId flowId,
        TimeSpan? maxLastRunIn = null,
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var services = flows.GetServices();
        await flows.EnsureStarted(flowId, cancellationToken).ConfigureAwait(false);

        var now = services.Clocks().SystemClock.Now;
        var @event = new FlowResetEvent(flowId, tag, now + maxLastRunIn);
        await flows.Notify(flowId, @event, ensureStarted: false, cancellationToken).ConfigureAwait(false);
    }
}
