using ActualLab.Internal;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowHost : ShardWorker, IHasServices
{
    private static readonly Requester Requester = new(typeof(FlowHost));
    private IFlows? _flows;

    public new IServiceProvider Services => base.Services;
    public FlowRegistry Registry { get; }
    public IFlows Flows => _flows ??= Services.GetRequiredService<IFlows>();
    public ICommander Commander { get; }
    public MomentClockSet Clocks { get; }

    public TimeSpan HandleEventRetryDelay { get; init; } = TimeSpan.FromSeconds(0.5);

    private FlowHostShard[] Shards { get; }

    public FlowHost(IServiceProvider services)
        : base(services, ShardScheme.FlowsBackend)
    {
        Registry = services.GetRequiredService<FlowRegistry>();
        Commander = services.Commander();
        Clocks = services.Clocks();

        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();
        Shards = Enumerable.Range(0, ShardScheme.ShardCount)
            .Select(i => new FlowHostShard(this, i, cancelledCts.Token))
            .ToArray();
    }

    public FlowWorklet this[FlowId flowId] {
        get {
            flowId.Require();
            var shardKey = ShardKeyResolvers.Get<FlowId>(Requester).Invoke(flowId);
            var shardIndex = ShardScheme.GetShardIndex(shardKey);

            var shard = Shards[shardIndex];
            if (shard.Worklets.TryGetValue(flowId, out var result))
                return result;

            lock (Shards) {
                shard = Shards[shardIndex];
                if (shard.Worklets.TryGetValue(flowId, out result))
                    return result;

                result = shard.NewWorklet(flowId).Start();
                shard.Worklets[flowId] = result;
                return result;
            }
        }
    }

    // The `long` it returns is DbFlow/FlowData.Version
    public async Task<long> HandleEvent(FlowId flowId, object? evt, CancellationToken cancellationToken)
    {
        while (true) {
            var worklet = this[flowId];
            var whenHandled = worklet.HandleEvent(evt, cancellationToken);
            try {
                return await whenHandled.ConfigureAwait(false);
            }
            catch (ChannelClosedException) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!worklet.StopToken.IsCancellationRequested)
                    throw; // It's some other ChannelClosedException - worklet.Channel isn't closed yet

                // FlowWorklet.OnRun implements a graceful stop, so it may take a while for it to complete.
                await worklet.WhenRunning!.ConfigureAwait(false);
                // Once the worklet is gone, we still want to wait a bit before we try use the next one -
                // that's because its new FlowHostShard may need to be re-locked & allocated, etc.
                await Task.Delay(HandleEventRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Protected methods

    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        FlowHostShard shard;
        lock (Shards) {
            shard = new FlowHostShard(this, shardIndex, cancellationToken);
            Shards[shardIndex] = shard;
        }
        return shard.OnRun(cancellationToken); // Delegate
    }
}
