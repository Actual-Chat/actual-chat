using ActualChat.Mesh;
using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class ShardWorker : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.ShardWorker;

    private ILogger? _log;

    protected IServiceProvider Services { get; }
    protected ILogger Log => _log ??= Services.LoggerFactory().CreateLogger(GetType().NonProxyType(), $"({ShardScheme.Id})");
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    protected MeshWatcher MeshWatcher { get; }
    protected ShardScheme ShardScheme { get; }
    protected IMeshLocks ShardLocks { get; }
    protected ShardState[] ShardStates { get; }
    protected string KeyPrefix { get; }

    public MeshNode ThisNode { get; }
    public MeshLockOptions LockOptions { get; init; }
    public RandomTimeSpan RepeatDelay { get; init; } = TimeSpan.FromMilliseconds(50).ToRandom(0.25);
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.1, 5);
    public MomentClock Clock => ShardLocks.Clock;

    protected ShardWorker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
        : base(services.HostLifetimeIfExist()?.ApplicationStopping.CreateLinkedTokenSource())
    {
        Services = services;
        ShardScheme = shardScheme;
        MeshWatcher = services.MeshWatcher();
        ThisNode = MeshWatcher.OwnNode;

        KeyPrefix = keyPrefix ?? GetType().Name;
        ShardLocks = GetMeshLocks(nameof(ShardLocks));
        LockOptions = ShardLocks.LockOptions;
        ShardStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardState(this, i)).ToArray();
    }

    protected IMeshLocks GetMeshLocks(string name)
    {
        var keyPrefix = KeyPrefix;
        if (keyPrefix.Length != 0)
            keyPrefix += ".";
        var fullKeyPrefix = $"{keyPrefix}{name}.{ShardScheme.Id.Value}";
        return Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(fullKeyPrefix);
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var usedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        try {
            var changes = MeshWatcher.State.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
            await foreach (var (state, error) in changes.ConfigureAwait(false)) {
                if (error != null) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "MeshWatcher.State returned an error, skipping");
                    continue;
                }

                addedShards.Clear();
                removedShards.Clear();
                var shardMap = state.GetShardMap(ShardScheme);
                var nodes = shardMap.Nodes;
                var nodeIndexes = shardMap.NodeIndexes;
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var shardState = ShardStates[shardIndex];
                    var mustUse = node == ThisNode;
                    if (mustUse == usedShards[shardIndex] && shardState is { WhenStopped.IsCompleted: false, StopToken.IsCancellationRequested: false })
                        continue;

                    usedShards[shardIndex] = mustUse;
                    (mustUse ? addedShards : removedShards).Add(shardIndex);
                    lock (Lock)
                        ShardStates[shardIndex] = shardState.NextState(mustUse);
                }
                if (addedShards.Count > 0 || removedShards.Count > 0)
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        usedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));
            }
        }
        finally {
            await Task.WhenAll(ShardStates.Select(x => x.DisposeAsync().AsTask())).SilentAwait(false);
        }
    }

    private async Task Use(int shardIndex, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (!cancellationToken.IsCancellationRequested) {
            DebugLog?.LogDebug("Shard #{ShardIndex}: ?? {ThisNodeId}", shardIndex, ThisNode.Ref);
            var lockHolder = await ShardLocks.Lock(shardIndex.Format(), "", cancellationToken).ConfigureAwait(false);
            var lockCts = cancellationToken.LinkWith(lockHolder.StopToken);
            var lockToken = lockCts.Token;
            var lockIsLost = false;
            Exception? error = null;
            try {
                DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId}", shardIndex, ThisNode.Ref);
                await OnRun(shardIndex, lockToken).ConfigureAwait(false);
                failureCount = 0;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                if (lockHolder.StopToken.IsCancellationRequested) {
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
                    Log.LogWarning("Shard #{ShardIndex}: -- {ThisNodeId} (shard lock is lost)", shardIndex, ThisNode.Ref);
                else
                    DebugLog?.LogDebug("Shard #{ShardIndex} -- {ThisNode}", shardIndex, ThisNode.Ref);
            }

            if (error != null) {
                var delay = RetryDelays[failureCount];
                Log.LogError(error, "Shard #{ShardIndex} @ {ThisNodeId}: OnRun failed, will retry in {Delay}",
                    shardIndex, ThisNode.Ref, delay.ToShortString());
                await Clock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else
                await Clock.Delay(RepeatDelay.Next(), cancellationToken).ConfigureAwait(false);
        }
    }

    // Nested types

    protected sealed class ShardState : IAsyncDisposable
    {
        private CancellationTokenSource StopTokenSource { get; }

        public ShardWorker Worker { get; }
        public int Index { get; }
        public CancellationToken StopToken { get; }
        public Task? WhenStopped { get; private set; }
        public bool IsRunning => WhenStopped != null && !StopToken.IsCancellationRequested;

        public ShardState(ShardWorker worker, int index)
        {
            Worker = worker;
            Index = index;
            StopTokenSource = Worker.StopToken.CreateLinkedTokenSource();
            StopToken = StopTokenSource.Token;
        }

        public ShardState NextState(bool mustUse)
            => mustUse
                ? Start()
                : Stop();

        private ShardState Start()
        {
            if (IsRunning)
                return this;

            var state = new ShardState(Worker, Index);
            state.WhenStopped = Worker.Use(Index, state.StopToken);
            return state;
        }

        private ShardState Stop()
        {
            StopTokenSource.CancelAndDisposeSilently();
            return this;
        }

        public ValueTask DisposeAsync()
        {
            StopTokenSource.CancelAndDisposeSilently();
            var whenStopped = WhenStopped;
            return whenStopped?.ToValueTask() ?? ValueTask.CompletedTask;
        }
    }
}
