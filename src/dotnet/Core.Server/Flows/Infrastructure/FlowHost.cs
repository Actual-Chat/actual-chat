using ActualLab.Rpc;

namespace ActualChat.Flows.Infrastructure;

public sealed class FlowHost : LegacyShardWorker, IHasServices
{
    private static readonly Requester Requester = new(typeof(FlowHost));

    private readonly FlowHostShard?[] _shards;

    public new IServiceProvider Services => base.Services;
    public FlowRegistry Registry { get; }

    [field: AllowNull, MaybeNull]
    public IFlows Flows => field ??= Services.GetRequiredService<IFlows>();
    public ICommander Commander { get; }
    public MomentClockSet Clocks { get; }

    public TimeSpan HandleEventRetryDelay { get; init; } = TimeSpan.FromSeconds(0.5);

    // TODO(AK): Why do we have single shard scheme for Flows? I'm sure we have to use service' shard scheme!
    public FlowHost(IServiceProvider services)
        : base(services, ShardScheme.FlowsBackend)
    {
        Registry = services.GetRequiredService<FlowRegistry>();
        Commander = services.Commander();
        Clocks = services.Clocks();
        _shards = new FlowHostShard?[ShardScheme.ShardCount];
    }

    // The `long` it returns is DbFlow/FlowData.Version
    public async Task<long> ProcessEvent(FlowId flowId, IFlowEvent evt, CancellationToken cancellationToken)
    {
        while (true) {
            var worklet = GetOrAddWorklet(flowId);
            try {
                var version = await worklet
                    .EnqueueAndProcessEvent(evt, cancellationToken)
                    .WaitAsync(cancellationToken) // It's important to have it here, read below
                    .ConfigureAwait(false);
                // .WaitAsync ensures that even if the queue is clogged,
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

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var shard = new FlowHostShard(this, shardIndex, cancellationToken);

        // Expose shard
        lock (_shards)
            _shards[shardIndex] = shard;

        // Await for the stop signal
        Log.LogInformation("+ FlowHost.OnRun({ShardIndex})", shardIndex);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
        Log.LogInformation("- FlowHost.OnRun({ShardIndex})", shardIndex);

        // Hide shard
        lock (_shards)
            _shards[shardIndex] = null;

        // Dispose all worklets
        while (true) {
            // ReSharper disable once InconsistentlySynchronizedField
            var disposeTasks = shard.Worklets
                .Select(w => w.DisposeAsync().AsTask())
                .ToList();
            if (disposeTasks.Count == 0)
                break;

            await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        }
        Log.LogInformation("-- FlowHost.OnRun({ShardIndex})", shardIndex);
    }

    // Private methods

    private LegacyFlowWorklet GetOrAddWorklet(FlowId flowId)
    {
        flowId.Require();
        var shardKey = ShardKeyResolvers.Get<FlowId>(Requester).Invoke(flowId);
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        // ReSharper disable once InconsistentlySynchronizedField
        var shard = _shards[shardIndex];
        return shard is null
            ? throw RpcRerouteException.MustReroute()
            : shard.GetOrAddWorklet(flowId);
    }
}
