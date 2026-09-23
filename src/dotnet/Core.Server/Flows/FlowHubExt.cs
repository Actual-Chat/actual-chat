namespace ActualChat.Flows;

public static class FlowHubExt
{
    // ThrottledUpdateFlow extensions

    public static ValueTask<bool> MustUpdate<TFlow>(
        this FlowHub flowHub, string target, CancellationToken cancellationToken = default)
        where TFlow : ThrottledUpdateFlow
        => flowHub.MustUpdate<TFlow>(target, addDependency: true, cancellationToken);

    public static async ValueTask<bool> MustUpdate<TFlow>(
        this FlowHub flowHub,
        string target,
        bool addDependency,
        CancellationToken cancellationToken = default)
        where TFlow : ThrottledUpdateFlow
    {
        var args = ThrottledUpdateFlow.GetArguments(target);
        var flow = addDependency
            ? await flowHub.TryGet<TFlow>(args, cancellationToken).ConfigureAwait(false)
            : await flowHub.TryGetIsolated<TFlow>(args, cancellationToken).ConfigureAwait(false);
        return flow?.MustUpdate(flowHub.SystemNow) ?? true;
    }

    public static async ValueTask<bool> TryScheduleUpdate<TFlow>(
        this FlowHub flowHub,
        string target,
        CancellationToken cancellationToken = default)
        where TFlow : ThrottledUpdateFlow
    {
        // A missing flow isn't created here: its first resume stores it. Get would store a blank
        // through Start, and Start schedules a resume of its own - a second chain next to this one.
        var args = ThrottledUpdateFlow.GetArguments(target);
        var flow = await flowHub.TryGetIsolated<TFlow>(args, cancellationToken).ConfigureAwait(false);
        if (flow is not null && !flow.MustUpdate(flowHub.SystemNow))
            return false;

        await flowHub.NewResumeEvent<TFlow>(args).Schedule(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // Private methods

    private static async ValueTask<TFlow?> TryGetIsolated<TFlow>(
        this FlowHub flowHub, string arguments, CancellationToken cancellationToken)
        where TFlow : Flow
    {
        using var _ = Computed.BeginIsolation();
        return await flowHub.TryGet<TFlow>(arguments, cancellationToken).ConfigureAwait(false);
    }
}
