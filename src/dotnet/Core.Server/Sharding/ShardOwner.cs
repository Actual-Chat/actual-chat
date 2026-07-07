using ActualLab.Diagnostics;
using ActualLab.Fusion.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed class ShardOwner : WorkerBase, IHasServices
{
    private static readonly TimeSpan LockToUseDelay = TimeSpan.FromSeconds(1);

    internal ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"@{ShardScheme.Name}");
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.ShardOwners);

    private readonly MutableState<bool?>[] _mustOwnStates;
    private readonly MutableState<BitArray> _bitmapState;
    private readonly MutableState<ShardState>[] _states;

    private IMeshLocks OwnershipLocks { get; }
    private MeshWatcher MeshWatcher => Host.MeshWatcher;
    private MeshNode ThisNode => Host.ThisNode;
    private StateFactory StateFactory => Host.StateFactory;
    private MomentClock Clock => Host.Clock;
    private RunnableDispatcher Dispatcher { get; } = new();

    public ShardOwners Host { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public MeshLockOptions LockOptions { get; }
    public IReadOnlyList<IState<bool?>> MustOwnStates { get; }
    public IState<BitArray> BitmapState { get; }
    public IReadOnlyList<IState<ShardState>> States { get; }

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
        MustOwnStates = _mustOwnStates = Enumerable
            .Range(0, shardScheme.ShardCount)
            .Select(_ => StateFactory.NewMutable(
                initialValue: (bool?)null,
                category: StateCategories.Get(GetType(), nameof(MustOwnStates))))
            .ToArray();
        States = _states = Enumerable
            .Range(0, shardScheme.ShardCount)
            .Select(i => StateFactory.NewMutable(
                initialValue: new ShardState(this, i),
                category: StateCategories.Get(GetType(), nameof(States))))
            .ToArray();
        BitmapState = _bitmapState = StateFactory.NewMutable(
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

    public Computed<ShardState> GetShardStateComputed<T>(T shardKey, bool addDependency)
        => GetShardStateComputed(ShardScheme.GetShardIndex(shardKey), addDependency);

    public Computed<ShardState> GetShardStateComputed(int shardKey, bool addDependency)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cShardState = States[shardIndex].Computed;
        var cCurrent = addDependency ? Computed.Current : null;
        if (cCurrent is not null)
            ComputedImpl.AddDependency(cCurrent, cShardState);
        return cShardState;
    }

    public ValueTask<ShardOwnership> RequireShardOwnership<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
        => RequireShardOwnership(ShardScheme.GetShardIndex(shardKey), addDependency, cancellationToken);

    public ValueTask<ShardOwnership> RequireShardOwnership(int shardKey, bool addDependency, CancellationToken cancellationToken)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        var cShardState = States[shardIndex].Computed;
        var shardState = cShardState.Value;
        var cCurrent = addDependency ? Computed.Current : null;
        var ownershipStatus = shardState.OwnershipStatus;
        switch (ownershipStatus) {
        case ShardOwnershipStatus.OwnedByThisNode when shardState.HasLiveOwnership:
            if (cCurrent is not null)
                ComputedImpl.AddDependency(cCurrent, cShardState);
            return new(shardState.Ownership!);
        case ShardOwnershipStatus.OwnedByThisNode: // The lock is lost, wait for it to be re-acquired
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
                .When(x => x.HasLiveOwnership || !x.MustOwn, FixedDelayer.YieldUnsafe, cancellationToken)
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
        // Wait for MeshWatcher to announce this node and discover other nodes.
        // Without this, the initial MeshState has only ThisNode, causing every node
        // to try locking ALL shards before discovering the actual mesh topology.
        await MeshWatcher.WhenAnnounced.WaitAsync(cancellationToken).ConfigureAwait(false);

        var bitmap = new BitArray(BitmapState.Value);
        var addedOwnShards = new List<int>();
        var removedOwnShards = new List<int>();
        var updateShardStateTasks = ShardScheme.ShardIndexes
            .Select(i => PushShardState(i, cancellationToken))
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
                    if (isOwn == _mustOwnStates[shardIndex].Value)
                        continue;

                    hasChanges = true;
                    (isOwn ? addedOwnShards : removedOwnShards).Add(shardIndex);
                    bitmap[shardIndex] = isOwn;
                    _mustOwnStates[shardIndex].Value = isOwn;
                }
                if (hasChanges) {
                    _bitmapState.Value = new BitArray(bitmap);
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

    private async Task PushShardState(int shardIndex, CancellationToken cancellationToken)
    {
        var mutableState = _states[shardIndex];
        try {
            var changes = MustOwnStates[shardIndex].Computed.Changes(FixedDelayer.YieldUnsafe, cancellationToken);
            await foreach (var computed in changes.ConfigureAwait(false)) {
                if (computed.Error is { } error) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "MustOwnStates[{ShardIndex}] returned an error, skipping", shardIndex);
                    continue;
                }

                if (computed.ValueOrDefault is not { } mustOwn)
                    continue; // Wait for mesh state to determine ownership

                var isOwn = mutableState.Value.OwnershipStatus is not ShardOwnershipStatus.MappedToOtherNode;
                if (isOwn != mustOwn)
                    mutableState.Value = new ShardState(mutableState.Value, mustOwn);

                if (mustOwn) {
                    var linkedCts = cancellationToken.CreateLinkedTokenSource();
                    var linkedToken = linkedCts.Token;
                    computed.Invalidated += _ => linkedCts.CancelAndDisposeSilently();

                    while (!linkedToken.IsCancellationRequested)
                        await LockAndUse(shardIndex, linkedToken).SilentAwait(false); // Doesn't throw!
                }
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "SyncShardState({ShardIndex}) failed", shardIndex);
        }
        finally {
            if (!mutableState.Value.IsFinal)
                mutableState.Value = new ShardState(mutableState.Value); // Final shard state
        }
    }

    private async Task LockAndUse(int shardIndex, CancellationToken cancellationToken)
    {
        var mutableState = _states[shardIndex];
        DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId}", shardIndex, ThisNode.Ref);

        var lockHolder = await OwnershipLocks.Lock(shardIndex.Format(), cancellationToken).ConfigureAwait(false);
        var lockToken = lockHolder.StopToken;
        DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId}", shardIndex, ThisNode.Ref);

        bool isAdded = false;
        ShardOwnership? ownership = null;
        var linkedCts = lockToken.LinkWith(cancellationToken);
        var linkedToken = linkedCts.Token;
        try {
            await Task.Delay(LockToUseDelay, linkedToken).ConfigureAwait(false);

            var ownedShardState = new ShardState(mutableState.Value, mustOwn: true, lockHolder);
            mutableState.Value = ownedShardState;
            ownership = ownedShardState.Ownership!;
            isAdded = Dispatcher.Add(ownership, throwIfDisposed: false);

            // Wait for lock loss OR cancellation (shard reassignment).
            // We must respond to cancellationToken so workers are stopped while the lock is still held,
            // preventing overlap with the new host's workers.
            await TaskExt.NeverEnding(linkedToken).SilentAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(linkedToken)) {
            Log.LogError(e, "Shard #{ShardIndex}: LockAndUse failed", shardIndex);
        }
        finally {
            linkedCts.CancelAndDisposeSilently();
        }

        // The lock is lost or the shard is reassigned: stop claiming ownership right away,
        // otherwise RequireShardOwnership keeps handing out an Ownership with a dead lock
        if (ownership != null && ReferenceEquals(mutableState.Value.Ownership, ownership))
            mutableState.Value = new ShardState(mutableState.Value, mustOwn: true);

        if (isAdded)
            await Dispatcher.Remove(ownership!).SilentAwait(false);
        if (lockToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            Log.LogWarning("Shard #{ShardIndex}: -- {ThisNodeId} - lost the lock", shardIndex, ThisNode.Ref);
        else
            DebugLog?.LogDebug("Shard #{ShardIndex}: -- {ThisNodeId}", shardIndex, ThisNode.Ref);
        await lockHolder.DisposeAsync().SilentAwait(false);
    }

    // Nested types

    public sealed class ShardState
    {
        public ShardOwner ShardOwner { get; }
        public int ShardIndex { get; }
        public bool IsFinal { get; }
        public bool MustOwn { get; }
        public ShardOwnership? Ownership { get; }
        public bool HasLiveOwnership => Ownership is { } ownership && !ownership.LockToken.IsCancellationRequested;
        public ShardOwnershipStatus OwnershipStatus { get; }
        public int Version { get; }

        internal ShardState(ShardOwner shardOwner, int shardIndex)
        {
            ShardOwner = shardOwner;
            ShardIndex = shardIndex;
            MustOwn = true;
            Ownership = null;
            OwnershipStatus = ShardOwnershipStatus.MappedToThisNode;
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
        }
    }
}
