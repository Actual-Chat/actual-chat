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
            if (shard.Worklets.TryGetValue(flowId, out var worklet))
                return worklet;

            lock (Shards) {
                shard = Shards[shardIndex];
                if (shard.Worklets.TryGetValue(flowId, out worklet))
                    return worklet;

                if (StopToken.IsCancellationRequested)
                    throw Errors.AlreadyDisposed<FlowHost>();

                worklet = shard.NewWorklet(flowId);
                shard.Worklets[flowId] = worklet;
                return worklet;
            }
        }
    }

    // The `long` it returns is DbFlow/FlowData.Version
    public async Task<long> ProcessEvent(FlowId flowId, IFlowEvent evt, CancellationToken cancellationToken)
    {
        while (true) {
            var worklet = this[flowId];
            try {
                var version = await worklet
                    .EnqueueAndProcessEvent(evt, cancellationToken)
                    .WaitAsync(cancellationToken) // It's important to have it here, read below
                    .ConfigureAwait(false);
                // .WaitAsync ensures that even if queue is clogged,
                // HandleEvent will instantly return on cancellationToken cancellation.
                return version;
            }
            catch (OperationCanceledException e) when (!e.IsCancellationOf(cancellationToken))  {
                if (!worklet.StopToken.IsCancellationRequested)
                    throw;

                // Worklet is dead - e.g. because its shard has lost the lock.
                // We'll try to spin up a new worklet here.
                await worklet.WhenRunning!.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Once the worklet is gone, we want to wait a bit before trying to spin up the next one -
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
