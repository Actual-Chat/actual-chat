using ActualChat.Rpc;

namespace ActualChat.Chat;

// Periodically probes every DiagnosticsBackend shard and verifies that both routing
// and computed invalidation work: a call must execute on the host the local shard map
// points to, and the long-lived probe computed must never get stuck on a former owner.
public sealed class ShardRoutingMonitor(IServiceProvider services) : WorkerBase
{
    private static readonly TimeSpan CheckPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CallTimeout = ServerConstants.Backend.LongTimeout;
    private const int MaxRetryCount = 2;

    private readonly Computed<string>?[] _probeComputeds = new Computed<string>?[ShardScheme.ShardCount];

    private static ShardScheme ShardScheme => ShardScheme.DiagnosticsBackend;

    private IDiagnosticsBackend Backend { get; } = services.GetRequiredService<IDiagnosticsBackend>();
    private MeshWatcher MeshWatcher { get; } = services.GetRequiredService<MeshRpcRefs>().MeshWatcher;
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async Task CheckShardWithRetries(int shardIndex, CancellationToken cancellationToken)
    {
        for (var retryIndex = 0; retryIndex <= MaxRetryCount; retryIndex++) {
            string? error;
            try {
                error = await CheckShard(shardIndex, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                error = $"probe call failed: {e.GetType().GetName()}({e.Message})";
            }
            if (error == null)
                return;

            if (retryIndex < MaxRetryCount) {
                Log.LogWarning("Shard #{ShardIndex}: {Error} - will recheck", shardIndex, error);
                await Clocks.CpuClock.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            else
                Log.LogError("Shard #{ShardIndex}: {Error} - persists after {RetryCount} rechecks",
                    shardIndex, error, MaxRetryCount);
        }
    }

    public async Task<string?> CheckShard(int shardIndex, CancellationToken cancellationToken)
    {
        // Returns null when the check passes or the shard map is in transition, otherwise the error text
        var expected = GetExpectedHostId(shardIndex);
        if (expected == null)
            return null; // The shard has no owner yet - the mesh is in transition

        var direct = await Backend.GetShardHostIdNonComputed(ShardKey.New(shardIndex), cancellationToken)
            .WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);

        var probeComputed = _probeComputeds[shardIndex];
        if (probeComputed == null)
            probeComputed = await Computed
                .Capture(() => Backend.GetShardHostId(ShardKey.New(shardIndex), cancellationToken), cancellationToken)
                .AsTask().WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);
        else {
            // A consistent computed is reused as-is: that's exactly what makes a frozen one detectable
            probeComputed = await probeComputed.Update(cancellationToken)
                .AsTask().WaitAsync(CallTimeout, cancellationToken).ConfigureAwait(false);
        }
        _probeComputeds[shardIndex] = probeComputed;
        var computed = probeComputed.Value;

        if (GetExpectedHostId(shardIndex) != expected)
            return null; // The shard has just migrated - skip this round

        if (direct != expected)
            return $"a direct call executed on '{direct}', expected '{expected}'";
        if (computed != expected)
            return $"the probe computed shows '{computed}', expected '{expected}' - likely frozen";

        return null;
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(ProbeAllShards)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(3, 60), Log)
            .CycleForever()
            .Run(cancellationToken);

    // Private methods

    private async Task ProbeAllShards(CancellationToken cancellationToken)
    {
        await MeshWatcher.WhenAnnounced.WaitAsync(cancellationToken).ConfigureAwait(false);
        await Clocks.CpuClock.Delay(CheckPeriod, cancellationToken).ConfigureAwait(false);
        for (var shardIndex = 0; shardIndex < ShardScheme.ShardCount; shardIndex++)
            await CheckShardWithRetries(shardIndex, cancellationToken).ConfigureAwait(false);
    }

    private string? GetExpectedHostId(int shardIndex)
        => MeshWatcher.State.Value.GetShardMap(ShardScheme)[shardIndex]?.Ref.Value;
}
