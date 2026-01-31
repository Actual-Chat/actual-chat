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
        var flow = await flowHub.Get<TFlow>(args, addDependency, cancellationToken).ConfigureAwait(false);
        return flow.MustUpdate(flowHub.SystemNow);
    }

    public static async ValueTask<bool> TryScheduleUpdate<TFlow>(
        this FlowHub flowHub,
        string target,
        CancellationToken cancellationToken = default)
        where TFlow : ThrottledUpdateFlow
    {
        var args = ThrottledUpdateFlow.GetArguments(target);
        var flow = await flowHub.Get<TFlow>(args, addDependency: false, cancellationToken).ConfigureAwait(false);
        if (!flow.MustUpdate(flowHub.SystemNow)) return false;

        await flowHub.NewResumeEvent<TFlow>(args).Schedule(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
