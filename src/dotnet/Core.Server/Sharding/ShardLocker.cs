using ActualChat.Mesh;
using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class ShardLocker : WorkerBase
{
    protected static bool DebugMode => Constants.DebugMode.ShardLocker;

    protected IServiceProvider Services { get; }

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType().NonProxyType(), $"({ShardScheme.Id})");
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    protected MeshWatcher MeshWatcher { get; }
    protected ShardScheme ShardScheme { get; }
    protected StateFactory StateFactory { get; }
    protected IMeshLocks ShardLocks { get; }

    public MeshNode ThisNode { get; }
    public string KeyPrefix { get; }
    public MeshLockOptions LockOptions { get; init; }
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.1, 5);
    public MomentClock Clock => ShardLocks.Clock;
    public IReadOnlyList<ShardState> ShardStates { get; protected set; } // You can't modify it

    protected ShardLocker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
        : base(services.HostLifetimeIfExist()?.ApplicationStopping.CreateLinkedTokenSource())
    {
        Services = services;
        ShardScheme = shardScheme;
        MeshWatcher = services.MeshWatcher();
        StateFactory = services.StateFactory();

        ThisNode = MeshWatcher.ThisNode;
        KeyPrefix = keyPrefix ?? GetType().GetName();
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

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lockedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        var disposeTasks = new List<Task>();
        try {
            var changes = MeshWatcher.State.Computed.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
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
                var nextShardStates = new ShardState[ShardScheme.ShardCount];
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var shardState = ShardStates[shardIndex];
                    var mustLock = node == ThisNode;
                    if (shardState.MustLock == mustLock) {
                        nextShardStates[shardIndex] = shardState;
                        continue;
                    }

                    var nextShardState = new ShardState(shardState, mustLock);
                    nextShardStates[shardIndex] = nextShardState;
                    if (shardState != nextShardState)
                        disposeTasks.Add(shardState.WhenDisposed);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                lock (Lock)
                    ShardStates = nextShardStates;
                if (addedShards.Count > 0 || removedShards.Count > 0)
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        lockedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));

                await Task.WhenAll(disposeTasks).SilentAwait(false);
                disposeTasks.Clear();
            }
        }
        finally {
            await Task.WhenAll(ShardStates.Select(x => x.DisposeAsync().AsTask())).SilentAwait(false);
        }
    }

    private async Task LockShard(ShardState shardState, ShardState previousState)
    {
        // We must make sure we don't run LockShard in parallel with the previous one.
        // We always cancel the previous one, but there is no guarantee that it will stop immediately.
        await previousState.WhenDisposed.SilentAwait(false);

        var shardIndex = shardState.Index;
        var unlockToken = shardState.CancelLockToken;
        var failureCount = 0;
        var shardLock = (ShardLock?)null;
        try {
            while (!unlockToken.IsCancellationRequested) {
                if (shardLock is null) {
                    DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId}", shardIndex, ThisNode.Ref);
                    var lockHolder = await ShardLocks.Lock(shardIndex.Format(), "", unlockToken).ConfigureAwait(false);
                    shardLock = new ShardLock(shardState, lockHolder);
                }
                try {
                    var unlockedToken = shardLock.Holder.StopToken;
                    unlockedToken.ThrowIfCancellationRequested(); // Maybe we don't need UseShard call
                    await UseShard(shardLock, unlockedToken).ConfigureAwait(false);
                    failureCount = 0;
                }
                catch (Exception e) when (!e.IsCancellationOf(unlockToken)) {
                    if (shardLock.IsLost)
                        failureCount = 0;
                    else {
                        failureCount++;
                        var delay = RetryDelays[failureCount];
                        Log.LogError(e,
                            "Shard #{ShardIndex} @ {ThisNodeId}: UseShard failed, will re-run it in {Delay}",
                            shardIndex,
                            ThisNode.Ref,
                            delay.ToShortString());
                        await Clock.Delay(delay, unlockToken).ConfigureAwait(false);
                    }
                }

                // We need this check here, coz shardLock could be lost during Clock.Delay as well
                if (!shardLock.IsLost)
                    continue;

                await shardLock.DisposeSilentlyAsync().ConfigureAwait(false);
                shardLock = null;
                Log.LogWarning("Shard #{ShardIndex}: -- {ThisNodeId} (lock is lost)", shardIndex, ThisNode.Ref);
            }
        }
        finally {
            if (shardLock is not null) {
                var isLost = shardLock.IsLost;
                await shardLock.DisposeSilentlyAsync().ConfigureAwait(false);
                if (!isLost)
                    Log.LogDebug("Shard #{ShardIndex}: -- {ThisNodeId}", shardIndex, ThisNode.Ref);
            }
        }
    }

    // This is the primary method to override in this class
    protected virtual Task UseShard(ShardLock shardLock, CancellationToken cancellationToken)
        => ActualLab.Async.TaskExt.NewNeverEndingUnreferenced().WaitAsync(cancellationToken);

    // Nested types

    public sealed class ShardState : IAsyncDisposable
    {
        private CancellationTokenSource CancelLockTokenSource { get; }

        public ShardLocker Owner { get; }
        public int Index { get; }
        public MutableState<ShardLock?> LockState { get; }
        public CancellationToken CancelLockToken { get; }
        public Task? LockTask { get; }
        public bool MustLock => LockTask != null;
        public Task WhenDisposed { get; }

        public ShardState(ShardLocker owner, int index)
        {
            Owner = owner;
            Index = index;
            LockState = Owner.StateFactory.NewMutable<ShardLock?>(
                category: StateCategories.Get(GetType(), nameof(LockState)));
            CancelLockTokenSource = Owner.StopToken.CreateLinkedTokenSource();
            CancelLockToken = CancelLockTokenSource.Token;
            CancelLockTokenSource.CancelAndDisposeSilently();
            WhenDisposed = Task.CompletedTask;
        }

        public ShardState(ShardState previousState, bool mustLock)
        {
            previousState.CancelLockTokenSource.CancelAndDisposeSilently();
            Owner = previousState.Owner;
            Index = previousState.Index;
            LockState = previousState.LockState;
            if (mustLock) {
                CancelLockTokenSource = Owner.StopToken.CreateLinkedTokenSource();
                CancelLockToken = CancelLockTokenSource.Token;
                LockTask = Owner.LockShard(this, previousState);
                WhenDisposed = LockTask;
            }
            else {
                CancelLockTokenSource = previousState.CancelLockTokenSource;
                CancelLockToken = previousState.CancelLockToken;
                WhenDisposed = previousState.WhenDisposed;
            }
        }

        public ValueTask DisposeAsync()
        {
            CancelLockTokenSource.CancelAndDisposeSilently();
            return WhenDisposed.ToValueTask();
        }
    }

    public sealed class ShardLock : IAsyncDisposable
    {
        public ShardState State { get; }
        public MeshLockHolder Holder { get; }
        public bool IsLost => Holder.StopToken.IsCancellationRequested && !State.CancelLockToken.IsCancellationRequested;

        public ShardLock(ShardState state, MeshLockHolder holder)
        {
            State = state;
            Holder = holder;
            state.LockState.Value = this;
            Holder.StopToken.Register(() => state.LockState.Value = null);
        }

        public ValueTask DisposeAsync()
            => Holder.DisposeAsync();
    }
}
