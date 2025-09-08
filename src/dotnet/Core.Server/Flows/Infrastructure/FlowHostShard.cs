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
            : new FlowWorklet(this, flowId).Start();

    public async Task OnRun(CancellationToken cancellationToken)
    {
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
        while (true) {
            var disposeTasks = Worklets.Values
                .Select(w => w.DisposeAsync().AsTask())
                .ToList();
            if (disposeTasks.Count == 0)
                break;

            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
    }
}
