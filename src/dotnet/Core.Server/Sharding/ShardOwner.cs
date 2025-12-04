using ActualLab.Diagnostics;
using ActualLab.Fusion.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed class ShardOwner : WorkerBase, IHasServices
{
    private static readonly TimeSpan PostReleaseInvalidationPeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Century = TimeSpan.FromDays(36_524);

    [field: AllowNull, MaybeNull]
    internal ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"@{ShardScheme.Name}");
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.ShardOwners);

    private IMeshLocks OwnershipLocks { get; }
    private MeshWatcher MeshWatcher => Host.MeshWatcher;
    private MeshNode ThisNode => Host.ThisNode;
    private StateFactory StateFactory => Host.StateFactory;
    private MomentClock Clock => Host.Clock;
    private RunnableDispatcher Dispatcher { get; } = new();
    private MutableState<bool?>[] MutableMustOwnStates { get; }
    private MutableState<ShardState>[] MutableShardStates { get; }
    private MutableState<BitArray> MutableBitmapState { get; }

    public ShardOwners Host { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public MeshLockOptions LockOptions { get; }
    public IReadOnlyList<IState<bool?>> MustOwnStates { get; }
    public IReadOnlyList<IState<ShardState>> ShardStates { get; }
    public IState<BitArray> BitmapState { get; }

    public ShardOwner(
        ShardOwners host,
        IMeshLocks ownershipLocks,
        ShardScheme shardScheme,
        CancellationTokenSource stopTokenSource)
        : base(stopTokenSource)
    {
        Host = host;
        Services = host.Services;
        ShardScheme = shardScheme;
        OwnershipLocks = ownershipLocks;
        LockOptions = ownershipLocks.LockOptions;
        MustOwnStates = MutableMustOwnStates = Enumerable
            .Range(0, shardScheme.ShardCount)
            .Select(_ => StateFactory.NewMutable(
                initialValue: (bool?)null,
                category: StateCategories.Get(GetType(), nameof(MustOwnStates))))
            .ToArray();
        ShardStates = MutableShardStates = Enumerable
            .Range(0, shardScheme.ShardCount)
            .Select(i => StateFactory.NewMutable(
                initialValue: new ShardState(this, i),
                category: StateCategories.Get(GetType(), nameof(ShardStates))))
            .ToArray();
        BitmapState = MutableBitmapState = StateFactory.NewMutable(
            initialValue: new BitArray(ShardScheme.ShardCount, true),
            category: StateCategories.Get(GetType(), nameof(BitmapState)));
    }

    public IAsyncDisposable Use(string name, Func<ShardOwnership, CancellationToken, Task> func, IRetryPolicy? retryPolicy)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock) { RetryPolicy = retryPolicy });
    public IAsyncDisposable Use(string name, Func<ShardOwnership, CancellationToken, Task> func)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock));
    public IAsyncDisposable Use(string name, Func<int, CancellationToken, Task> func, IRetryPolicy? retryPolicy)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock) { RetryPolicy = retryPolicy });
    public IAsyncDisposable Use(string name, Func<int, CancellationToken, Task> func)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock));
    public IAsyncDisposable Use(ShardRunnable shardRunnable)
        => Dispatcher.Use(shardRunnable);

    public ShardState GetShardState<T>(T shardKey)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        return ShardStates[shardIndex].Value;
    }

    public Computed<ShardState> GetShardStateComputed<T>(T shardKey, bool addDependency)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cShardState = ShardStates[shardIndex].Computed;
        var cCurrent = addDependency ? Computed.Current : null;
        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cShardState);
        return cShardState;
    }

    public ShardOwnershipStatus GetShardOwnershipStatus<T>(T shardKey, bool addDependency)
    {
        var cShardState = GetShardStateComputed(shardKey, addDependency);
        return cShardState.Value.OwnershipStatus;
    }

    public ValueTask<ShardOwnership> RequireShardOwnership<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cShardState = ShardStates[shardIndex].Computed;
        var shardState = cShardState.Value;
        var cCurrent = addDependency ? Computed.Current : null;
        var ownershipStatus = shardState.OwnershipStatus;
        switch (ownershipStatus) {
        case ShardOwnershipStatus.OwnedByThisNode:
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cShardState);
            return new(shardState.Ownership!);
        case ShardOwnershipStatus.MappedToThisNode:
            return CompleteAsync();
        case ShardOwnershipStatus.MappedToOtherNode:
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cShardState);
            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        default:
            throw StandardError.Internal($"Invalid ShardOwnershipStatus value: {ownershipStatus}.");
        }

        async ValueTask<ShardOwnership> CompleteAsync() {
            cShardState = await cShardState
                .When(x => x.Ownership is not null || !x.MustOwn, FixedDelayer.YieldUnsafe, cancellationToken)
                .ConfigureAwait(false);
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cShardState);
            if (cShardState.Value.Ownership is { } ownership)
                return ownership;
            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
        }
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var bitmap = new BitArray(BitmapState.Value);
        var addedOwnShards = new List<int>();
        var removedOwnShards = new List<int>();
        var updateShardStateTasks = ShardScheme.ShardIndexes
            .Select(i => SyncShardState(i, cancellationToken))
            .ToArray();
        try {
            var changes = Host.MeshWatcher.State.Computed.Changes(FixedDelayer.YieldUnsafe, cancellationToken);
            await foreach (var (meshState, error) in changes.ConfigureAwait(false)) {
                if (error != null) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "MeshWatcher.State returned an error, skipping");
                    continue;
                }

                var shardMap = meshState.GetShardMap(ShardScheme);
                var nodes = shardMap.Nodes;
                var nodeIndexes = shardMap.NodeIndexes;
                var hasChanges = false;
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var isOwn = node == ThisNode;
                    if (isOwn == MutableMustOwnStates[shardIndex].Value)
                        continue;

                    hasChanges = true;
                    (isOwn ? addedOwnShards : removedOwnShards).Add(shardIndex);
                    bitmap[shardIndex] = isOwn;
                    MutableMustOwnStates[shardIndex].Value = isOwn;
                }
                if (hasChanges) {
                    MutableBitmapState.Value = new BitArray(bitmap);
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        bitmap.Format(),
                        addedOwnShards.ToDelimitedString(","),
                        removedOwnShards.ToDelimitedString(","));
                }

                addedOwnShards.Clear();
                removedOwnShards.Clear();
            }
        }
        finally {
            _ = DisposeAsync();
            await Task.WhenAll(updateShardStateTasks).SilentAwait(false);
        }
    }

    // Private methods

    private async Task SyncShardState(int shardIndex, CancellationToken cancellationToken)
    {
        var mutableState = MutableShardStates[shardIndex];
        try {
            var changes = MustOwnStates[shardIndex].Computed.Changes(FixedDelayer.YieldUnsafe, cancellationToken);
            await foreach (var computed in changes.ConfigureAwait(false)) {
                var isOwn = computed.ValueOrDefault ?? false;
                if (computed.Error is { } error) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "IsOwnShardStates[{ShardIndex}] returned an error, skipping", shardIndex);
                    continue;
                }
                if (!isOwn) {
                    if (mutableState.Value.Version == 0)
                        mutableState.Value = new ShardState(mutableState.Value, false);
                    continue;
                }

                var linkedCts = cancellationToken.CreateLinkedTokenSource();
                var linkedToken = linkedCts.Token;
                computed.Invalidated += _ => linkedCts.CancelAndDisposeSilently();

                while (!linkedToken.IsCancellationRequested) {
                    var shardState = mutableState.Value;
                    if (shardState.OwnershipStatus is not ShardOwnershipStatus.MappedToThisNode)
                        mutableState.Value = new ShardState(shardState, true);
                    await LockShard(shardIndex, linkedToken).SilentAwait(false);
                }
                if (mutableState.Value.OwnershipStatus is not ShardOwnershipStatus.MappedToOtherNode)
                    mutableState.Value = new ShardState(mutableState.Value, false);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "SyncShardState({ShardIndex}) failed", shardIndex);
        }
        finally {
            mutableState.Value = new ShardState(mutableState.Value); // Final shard state
        }
    }

    private async Task LockShard(int shardIndex, CancellationToken cancellationToken)
    {
        var mutableState = MutableShardStates[shardIndex];
        DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId}", shardIndex, ThisNode.Ref);
        var lockHolder = await OwnershipLocks.Lock(shardIndex.Format(), cancellationToken).ConfigureAwait(false);
        var lockToken = lockHolder.StopToken;

        DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId}", shardIndex, ThisNode.Ref);
        try {
            var ownedShardState = new ShardState(mutableState.Value, true, lockHolder);
            var ownership = ownedShardState.Ownership!;
            mutableState.Value = ownedShardState;
            Dispatcher.Add(ownership);
            await TaskExt.NeverEnding(lockToken).SilentAwait(false);
            await Dispatcher.Remove(ownership).SilentAwait(false);
        }
        finally {
            if (lockToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                Log.LogWarning("Shard #{ShardIndex}: -- {ThisNodeId} - lost the lock", shardIndex, ThisNode.Ref);
            else
                DebugLog?.LogDebug("Shard #{ShardIndex}: -- {ThisNodeId}", shardIndex, ThisNode.Ref);
            await lockHolder.DisposeAsync().SilentAwait(false);
        }
    }

    // Nested types

    public sealed class ShardState
    {
        public ShardOwner ShardOwner { get; }
        public int ShardIndex { get; }
        public bool IsFinal { get; }
        public bool MustOwn { get; }
        public ShardOwnership? Ownership { get; }
        public ShardOwnershipStatus OwnershipStatus { get; }
        public AsyncState<ShardState> AsyncState { get; }
        public int Version { get; }

        internal ShardState(ShardOwner shardOwner, int shardIndex)
        {
            ShardOwner = shardOwner;
            ShardIndex = shardIndex;
            MustOwn = true;
            Ownership = null;
            OwnershipStatus = ShardOwnershipStatus.MappedToThisNode;
            AsyncState = new AsyncState<ShardState>(this);
        }

        internal ShardState(ShardState prevState, bool mustOwn, MeshLockHolder? lockHolder = null)
        {
            ShardOwner = prevState.ShardOwner;
            ShardIndex = prevState.ShardIndex;
            MustOwn = mustOwn;
            if (lockHolder is null) {
                OwnershipStatus = MustOwn
                    ? ShardOwnershipStatus.MappedToThisNode
                    : ShardOwnershipStatus.MappedToOtherNode;
            }
            else {
                OwnershipStatus = ShardOwnershipStatus.OwnedByThisNode;
                Ownership = new ShardOwnership(this, lockHolder);
            }
            Version = prevState.Version + 1;
            AsyncState = prevState.AsyncState.TrySetNext(this);
        }

        internal ShardState(ShardState prevState)
        {
            ShardOwner = prevState.ShardOwner;
            ShardIndex = prevState.ShardIndex;
            IsFinal = true;
            MustOwn = prevState.MustOwn;
            OwnershipStatus = MustOwn
                ? ShardOwnershipStatus.MappedToThisNode
                : ShardOwnershipStatus.MappedToOtherNode;
            Version = prevState.Version + 1;
            AsyncState = prevState.AsyncState.TrySetNext(this);
        }

        public ValueTask<ShardOwnership> RequireShardOwnership(CancellationToken cancellationToken)
        {
            switch (OwnershipStatus) {
            case ShardOwnershipStatus.OwnedByThisNode:
                return new(Ownership!);
            case ShardOwnershipStatus.MappedToThisNode:
                return CompleteAsync();
            case ShardOwnershipStatus.MappedToOtherNode:
                throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
            default:
                throw StandardError.Internal($"Invalid ShardOwnershipStatus value: {OwnershipStatus}.");
            }

            async ValueTask<ShardOwnership> CompleteAsync() {
                var asyncState = await AsyncState
                    .When(x => x.Ownership is not null || !x.MustOwn, cancellationToken)
                    .ConfigureAwait(false);
                if (asyncState.Value.Ownership is { } ownership)
                    return ownership;
                throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");
            }
        }
    }
}
