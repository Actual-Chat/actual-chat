namespace ActualChat.Flows.Infrastructure;

public sealed class FlowHostShard(FlowHost host, int shardIndex, CancellationToken stopToken)
{
    private readonly ConcurrentDictionary<FlowId, LegacyFlowWorklet> _worklets = new();

    public FlowHost Host { get; } = host;
    public int ShardIndex { get; } = shardIndex;
    public CancellationToken StopToken { get; } = stopToken;
    public IEnumerable<LegacyFlowWorklet> Worklets => _worklets.Values;

    public LegacyFlowWorklet GetOrAddWorklet(FlowId flowId)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (_worklets.TryGetValue(flowId, out var worklet))
            return worklet;
        lock (_worklets) { // Double check locking
            if (_worklets.TryGetValue(flowId, out worklet))
                return worklet;

            worklet = new LegacyFlowWorklet(this, flowId);
            _worklets.TryAdd(flowId, worklet); // Must succeed
        }
        return worklet.Start();
    }

    public bool TryRemoveWorklet(LegacyFlowWorklet flowWorklet)
    {
        lock (_worklets)
            return _worklets.TryRemove(flowWorklet.FlowId, flowWorklet);
    }
}
