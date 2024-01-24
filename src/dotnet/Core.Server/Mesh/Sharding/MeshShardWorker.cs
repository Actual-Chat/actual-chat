namespace ActualChat.Mesh;

public abstract class MeshShardWorker<TShardingDef>(IServiceProvider services, string? keySuffix = null)
    : MeshShardWorker(services, TShardingDef.Instance, keySuffix)
    where TShardingDef : MeshShardingDef, IMeshShardingDef<TShardingDef>;

public abstract class MeshShardWorker : WorkerBase
{
    private ILogger? _log;

    protected IServiceProvider Services { get; }
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    protected MeshShardingDef ShardingDef { get; }
    protected IMeshLocks ShardLocks { get; }
    protected MeshWatcher MeshWatcher { get; }
    protected ShardState[] ShardStates { get; }

    public MeshNode ThisNode { get; }
    public MeshLockOptions LockOptions { get; init; }
    public RandomTimeSpan RepeatDelay { get; init; } = TimeSpan.FromMilliseconds(50).ToRandom(0.25);
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.1, 5);
    public IMomentClock Clock => ShardLocks.Clock;

    protected MeshShardWorker(IServiceProvider services, MeshShardingDef shardingDef, string? keySuffix = null)
    {
        Services = services;
        ShardingDef = shardingDef;
        keySuffix ??= GetType().Name;
        if (keySuffix.Length != 0)
            keySuffix = "." + keySuffix;
        var fullKeySuffix = $"{nameof(ShardLocks)}.{shardingDef.HostRole.Value}{keySuffix}";
        ShardLocks = services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(fullKeySuffix);
        LockOptions = ShardLocks.LockOptions;
        MeshWatcher = services.MeshWatcher();
        ThisNode = MeshWatcher.ThisNode;
        ShardStates = Enumerable.Range(0, shardingDef.Size).Select(i => new ShardState(this, i)).ToArray();
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        MeshWatcher.Start();
        var usedShards = new BitArray(ShardingDef.Size);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        try {
            var changes = MeshWatcher.State.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
            await foreach (var (state, error) in changes.ConfigureAwait(false)) {
                if (error != null) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "MeshWatcher.State returned an error, skipping");
                    continue;
                }

                addedShards.Clear();
                removedShards.Clear();
                var sharding = state.GetSharding(ShardingDef);
                for (var shardIndex = 0; shardIndex < sharding.Shards.Length; shardIndex++) {
                    var node = sharding.Shards[shardIndex];
                    var shardState = ShardStates[shardIndex];
                    var mustUse = node == ThisNode;
                    if (mustUse == usedShards[shardIndex])
                        continue;

                    usedShards[shardIndex] = mustUse;
                    (mustUse ? addedShards : removedShards).Add(shardIndex);
                    ShardStates[shardIndex] = shardState.NextState(mustUse);
                }
                if (addedShards.Count > 0 || removedShards.Count > 0)
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} (+{AddedShards}, -{RemovedShards})",
                        ThisNode.Id,
                        usedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));
            }
        }
        finally {
            await Task.WhenAll(ShardStates.Select(x => x.Stop())).SilentAwait();
        }
    }

    private async Task Use(int shardIndex, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (!cancellationToken.IsCancellationRequested) {
            var lockHolder = await ShardLocks.Lock(shardIndex.Format(), "", cancellationToken).ConfigureAwait(false);
            var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);
            var lockToken = lockCts.Token;
            var lockIsLost = false;
            Exception? error = null;
            try {
                Log.LogInformation("Shard #{ShardIndex} -> {ThisNodeId}", shardIndex, ThisNode.Id);
                await OnRun(shardIndex, lockToken).ConfigureAwait(false);
                failureCount = 0;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (lockToken.IsCancellationRequested) {
                    lockIsLost = true;
                    failureCount = 0;
                }
                else {
                    error = e;
                    failureCount++;
                }
            }
            finally {
                lockCts.DisposeSilently();
                await lockHolder.DisposeSilentlyAsync().ConfigureAwait(false);
                if (lockIsLost)
                    Log.LogWarning("Shard #{ShardIndex} <- {ThisNodeId} (shard lock is lost)", shardIndex, ThisNode.Id);
                else
                    Log.LogInformation("Shard #{ShardIndex} <- {ThisNode}", shardIndex, ThisNode.Id);
            }

            if (error != null) {
                var delay = RetryDelays[failureCount];
                Log.LogError(error, "Shard #{ShardIndex} @ {ThisNodeId}: OnRun failed, will retry in {Delay}",
                    shardIndex, ThisNode.Id, delay.ToShortString());
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else
                await Clock.Delay(RepeatDelay.Next(), cancellationToken).ConfigureAwait(false);
        }
    }

    // Nested types

    protected sealed class ShardState
    {
        private CancellationTokenSource StopTokenSource { get; }

        public MeshShardWorker Worker { get; }
        public int Index { get; }
        public CancellationToken StopToken { get; }
        public Task? WhenStopped { get; set; }

        public ShardState(MeshShardWorker worker, int index)
        {
            Worker = worker;
            Index = index;
            StopTokenSource = new();
            StopToken = StopTokenSource.Token;
        }

        public ShardState NextState(bool mustUse)
        {
            if (mustUse)
                return Start();

            StopTokenSource.CancelAndDisposeSilently();
            return this;
        }

        public ShardState Start()
        {
            var result = WhenStopped == null ? this : new ShardState(Worker, Index);
            result.WhenStopped = Worker.Use(Index, StopToken);
            return result;
        }

        public Task Stop()
        {
            StopTokenSource.CancelAndDisposeSilently();
            return WhenStopped ?? Task.CompletedTask;
        }
    }
}
