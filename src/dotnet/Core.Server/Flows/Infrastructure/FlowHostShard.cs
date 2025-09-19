namespace ActualChat.Flows.Infrastructure;

public sealed class FlowHostShard(FlowHost host, int shardIndex, CancellationToken stopToken)
{
    private readonly ConcurrentDictionary<FlowId, FlowWorklet> _worklets = new();

    public FlowHost Host { get; } = host;
    public int ShardIndex { get; } = shardIndex;
    public CancellationToken StopToken { get; } = stopToken;
    public IEnumerable<FlowWorklet> Worklets => _worklets.Values;

    public FlowWorklet GetOrAddWorklet(FlowId flowId)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (_worklets.TryGetValue(flowId, out var worklet))
            return worklet;
        lock (_worklets) { // Double check locking
            if (_worklets.TryGetValue(flowId, out worklet))
                return worklet;

            worklet = new FlowWorklet(this, flowId);
            _worklets.TryAdd(flowId, worklet); // Must succeed
        }
        return worklet.Start();
    }

    public bool TryRemoveWorklet(FlowWorklet flowWorklet)
    {
        lock (_worklets)
            return _worklets.TryRemove(flowWorklet.FlowId, flowWorklet);
    }
}
