namespace ActualChat.Flows.Infrastructure;

public sealed class FlowHostShard(FlowHost host, int shardIndex, CancellationToken stopToken)
{
    public FlowHost Host { get; } = host;
    public int ShardIndex { get; } = shardIndex;
    public CancellationToken StopToken { get; } = stopToken;
    public ConcurrentDictionary<FlowId, FlowWorklet> Worklets { get; } = new();

    public FlowWorklet NewWorklet(FlowId flowId)
        => StopToken.IsCancellationRequested
            ? throw StandardError.WrongShard<FlowId>()
            : new FlowWorklet(this, flowId);

    public async Task OnRun(CancellationToken cancellationToken)
    {
        using var dTask = cancellationToken.ToTask();
        await dTask.Resource.SilentAwait();

        // At this point
        var disposeTasks = new List<Task>();
        foreach (var (_, worklet) in Worklets)
            disposeTasks.Add(worklet.DisposeAsync().AsTask());
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
    }
}
