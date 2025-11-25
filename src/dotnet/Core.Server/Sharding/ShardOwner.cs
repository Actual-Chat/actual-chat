using ActualLab.Diagnostics;
using ActualLab.Fusion.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed class ShardOwner : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardOwner;
    private static readonly TimeSpan PostReleaseInvalidationPeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Century = TimeSpan.FromDays(36_524);

    [field: AllowNull, MaybeNull]
    internal ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"@{ShardScheme.Name}");
    private ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    private IMeshLocks OwnershipLocks { get; }
    private MeshWatcher MeshWatcher => Host.MeshWatcher;
    private MeshNode ThisNode => Host.ThisNode;
    private StateFactory StateFactory => Host.StateFactory;
    private MomentClock Clock => Host.Clock;
    private RunnableDispatcher Dispatcher { get; } = new();

    public ShardOwners Host { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public MeshLockOptions LockOptions { get; init; }
    public MutableState<OwnState> State { get; }

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

        var shardStates = Enumerable.Range(0, shardScheme.ShardCount)
            .Select(i => new ShardState(this, i, 0))
            .ToArray();
        State = StateFactory.NewMutable(
            initialValue: new OwnState(this, shardStates, 0),
            category: StateCategories.Get(GetType(), nameof(State)));
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

    public ShardState GetShardState(int shardIndex)
    {
        shardIndex = ShardScheme.GetShardIndex(shardIndex);
        return State.Computed.Value.ShardStates[shardIndex];
    }

    public ShardState GetShardState<T>(T shardKey)
    {
        var shardIndex = ShardScheme.GetShardIndex(shardKey);
        return State.Computed.Value.ShardStates[shardIndex];
    }

    public bool MustInvalidate<T>(T shardKey)
    {
        var invalidateUntil = GetShardState(shardKey).InvalidateUntilState.Value;
        return invalidateUntil >= Clock.Now;
    }

    public ShardOwnershipStatus GetShardOwnershipState<T>(T shardKey, bool addDependency = true)
        => GetShardState(shardKey).GetOwnershipStatus(addDependency);

    public Task<ShardOwnership> RequireOwnership<T>(T shardKey, CancellationToken cancellationToken)
        => GetShardState(shardKey).RequireOwnership(addDependency: true, cancellationToken);
    public Task<ShardOwnership> RequireOwnership<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
        => GetShardState(shardKey).RequireOwnership(addDependency, cancellationToken);

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lockedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        var disposeTasks = new List<Task>();
        var shardStates = State.Value.ShardStates; // Initial value
        var version = 1;
        try {
            var changes = Host.MeshWatcher.State.Computed.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
            await foreach (var (meshState, error) in changes.ConfigureAwait(false)) {
                if (error != null) {
                    if (error is ObjectDisposedException)
                        return;

                    Log.LogError(error, "MeshWatcher.State returned an error, skipping");
                    continue;
                }

                addedShards.Clear();
                removedShards.Clear();
                var shardMap = meshState.GetShardMap(ShardScheme);
                var nodes = shardMap.Nodes;
                var nodeIndexes = shardMap.NodeIndexes;
                var nextShardStates = new ShardState[shardStates.Count];
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var lockState = shardStates[shardIndex];
                    var mustLock = node == ThisNode;
                    if (lockState.MustLock == mustLock) {
                        nextShardStates[shardIndex] = lockState;
                        continue;
                    }

                    disposeTasks.Add(lockState.WhenDisposed);
                    nextShardStates[shardIndex] = new ShardState(lockState, mustLock, version);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                if (addedShards.Count > 0 || removedShards.Count > 0) {
                    // ReSharper disable once HeapView.CanAvoidClosure
                    State.Value = new OwnState(this, shardStates = nextShardStates, version);
                    Log.LogInformation("Shards @ {ThisNodeId}, v{Version}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref, version,
                        lockedShards.Format(),
                        addedShards.ToDelimitedString(","),
                        removedShards.ToDelimitedString(","));
                    version++;
                }

                await Task.WhenAll(disposeTasks).SilentAwait(false);
                disposeTasks.Clear();
            }
        }
        finally {
            State.SetError(new ObjectDisposedException(nameof(ShardState)));
            await Task.WhenAll(shardStates.Select(x => x.DisposeAsync().AsTask())).SilentAwait(false);
        }
    }

    // Private methods

    private async Task LockAndUseShard(ShardState shardState, ShardState prevShardState)
    {
        // We must make sure we don't run LockShard in parallel with the previous one.
        // We always cancel the previous one, but there is no guarantee that it will stop immediately.
        await prevShardState.WhenDisposed.SilentAwait(false);

        var shardIndex = shardState.ShardIndex;
        var cancelLockToken = shardState.CancelLockToken;
        for (var index = 1; !cancelLockToken.IsCancellationRequested; index++) {
            // Acquire the lock
            DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);
            var lockHolder = await OwnershipLocks.Lock(shardIndex.Format(), "", cancelLockToken).ConfigureAwait(false);
            await using var _1 = lockHolder.ConfigureAwait(false);
            var lockToken = lockHolder.StopToken;
            DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);

            // Create the processor
            if (!lockToken.IsCancellationRequested) {
                var shardOwnership = new ShardOwnership(shardState, lockHolder);
                Computed cMustInvalidateUntil;
                lock (shardState.MutableStateChangeLock) {
                    shardState.MutableInvalidateUntilState.Value = shardOwnership.AcquiredAt + Century; // We want it to change
                    cMustInvalidateUntil = shardState.InvalidateUntilState.Computed;
                    shardState.MutableOwnershipState.Value = shardOwnership;
                }
                Dispatcher.Add(shardOwnership);
                try {
                    await TaskExt.NeverEnding(lockToken).SilentAwait(false);
                }
                finally {
                    lock (shardState.MutableStateChangeLock)
                        shardState.MutableOwnershipState.Value = null;
                    await Dispatcher.Remove(shardOwnership).SilentAwait(false);
                    // Delay MutableInvalidateUntilState update by PostReleaseInvalidationPeriod,
                    // and make sure we don't overwrite the new value change it only if it wasn't changed.
                    _ = Task
                        .Delay(PostReleaseInvalidationPeriod, CancellationToken.None)
                        .ContinueWith(_ => {
                            if (!cMustInvalidateUntil.IsConsistent()) return;
                            lock (shardState.MutableStateChangeLock) {
                                if (!cMustInvalidateUntil.IsConsistent()) return;
                                shardState.MutableInvalidateUntilState.Value = Clock.Now;
                            }
                        }, TaskScheduler.Default);
                }
            }

            if (cancelLockToken.IsCancellationRequested)
                break;

            Log.LogWarning(
                "Shard #{ShardIndex}: -- {ThisNodeId} - lost the lock (#{Index})",
                shardIndex, ThisNode.Ref, index);
        }
    }

    // Nested types

    public sealed class OwnState(
        ShardOwner shardOwner,
        IReadOnlyList<ShardState> shardStates,
        int version)
    {
        public ShardOwner ShardOwner { get; } = shardOwner;
        public IReadOnlyList<ShardState> ShardStates { get; } = shardStates;
        public int Version { get; } = version;
        public Moment CreatedAt { get; } = shardOwner.Host.Clock.Now;
        public TimeSpan Age => ShardOwner.Host.Clock.Now - CreatedAt;
    }

    public sealed class ShardState : IAsyncDisposable
    {
        private readonly Task? _lockTask;
        private readonly CancellationTokenSource _cancelLockTokenSource;

        internal readonly Lock MutableStateChangeLock = new();
        internal readonly MutableState<ShardOwnership?> MutableOwnershipState;
        internal readonly MutableState<Moment> MutableInvalidateUntilState;

        public ShardOwner ShardOwner { get; }
        public int ShardIndex { get; }
        public long ShardOwnerStateVersion { get; }
        public bool MustLock => _lockTask != null; // or !CancelLockToken.IsCancellationRequested
        public CancellationToken CancelLockToken { get; }
        public IState<ShardOwnership?> OwnershipState => MutableOwnershipState;
        public IState<Moment> InvalidateUntilState => MutableInvalidateUntilState;
        public Task WhenDisposed { get; }

        internal ShardState(ShardOwner shardOwner, int shardIndex, int shardOwnerStateVersion)
        {
            ShardOwner = shardOwner;
            ShardIndex = shardIndex;
            ShardOwnerStateVersion = shardOwnerStateVersion;
            var stateFactory = ShardOwner.Host.StateFactory;
            MutableOwnershipState = stateFactory.NewMutable<ShardOwnership?>(
                category: StateCategories.Get(GetType(), nameof(OwnershipState)));
            MutableInvalidateUntilState = stateFactory.NewMutable<Moment>(
                category: StateCategories.Get(GetType(), nameof(InvalidateUntilState)));
            _cancelLockTokenSource = ShardOwner.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            _cancelLockTokenSource.CancelAndDisposeSilently(); // ~ MustLock = false
            WhenDisposed = Task.CompletedTask;
        }

        internal ShardState(ShardState prevState, bool mustLock, int shardOwnerStateVersion)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            ShardOwner = prevState.ShardOwner;
            ShardIndex = prevState.ShardIndex;
            ShardOwnerStateVersion = shardOwnerStateVersion;
            MutableOwnershipState = prevState.MutableOwnershipState;
            MutableInvalidateUntilState = prevState.MutableInvalidateUntilState;
            if (mustLock) {
                _cancelLockTokenSource = ShardOwner.StopToken.CreateLinkedTokenSource();
                CancelLockToken = _cancelLockTokenSource.Token;
                WhenDisposed = _lockTask = ShardOwner.LockAndUseShard(this, prevState);
            }
            else {
                _cancelLockTokenSource = prevState._cancelLockTokenSource;
                CancelLockToken = prevState.CancelLockToken;
                WhenDisposed = prevState.WhenDisposed;
            }
        }

        public ValueTask DisposeAsync()
        {
            MutableOwnershipState.SetError(new ObjectDisposedException(nameof(ShardState)));
            _cancelLockTokenSource.CancelAndDisposeSilently();
            return WhenDisposed.ToValueTask();
        }

        public ShardOwnershipStatus GetOwnershipStatus(bool addDependency = true)
        {
            var cCurrent = addDependency ? Computed.Current : null;
            var cOwnershipState = OwnershipState.Computed;
            if (cOwnershipState.Value is not null) {
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return ShardOwnershipStatus.LockedByThisNode;
            }

            if (MustLock) {
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return ShardOwnershipStatus.MappedToThisNode;
            }

            if (cCurrent is not null) {
                var cState = ShardOwner.State.Computed;
                if (cState.Value.Version != ShardOwnerStateVersion)
                    cCurrent.Invalidate(immediately: true); // ShardOwner.State has changed
                else
                    ComputedImpl.AddDependency(cCurrent, cState);
            }
            return ShardOwnershipStatus.MappedToOtherNode;
        }

        public Task<ShardOwnership> RequireOwnership(CancellationToken cancellationToken)
            => RequireOwnership(addDependency: true, cancellationToken);
        public Task<ShardOwnership> RequireOwnership(bool addDependency, CancellationToken cancellationToken)
        {
            var cCurrent = addDependency ? Computed.Current : null;
            var cOwnershipState = OwnershipState.Computed;
            if (cOwnershipState.Value is not null) {
                // ShardOwnershipStatus.LockedByThisNode
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return (Task<ShardOwnership>)cOwnershipState.GetValuePromise();
            }

            if (MustLock) {
                // ShardOwnershipStatus.MappedToThisNode
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return CompleteAsync();
            }

            // ShardOwnershipStatus.MappedToOtherNode
            if (cCurrent is not null) {
                var cState = ShardOwner.State.Computed;
                if (cState.Value.Version != ShardOwnerStateVersion)
                    cCurrent.Invalidate(immediately: true); // ShardOwner.State has changed
                else
                    ComputedImpl.AddDependency(cCurrent, cState);
            }
            throw new RpcRerouteException("The shard isn't mapped to this node.");

            async Task<ShardOwnership> CompleteAsync() {
                var linkedCts = cancellationToken.LinkWith(CancelLockToken);
                var linkedToken = linkedCts.Token;
                try {
                    cOwnershipState = await cOwnershipState
                        .When(x => x is not null, linkedToken)
                        .ConfigureAwait(false);
                    return cOwnershipState.Value!;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    // If we're here, CancelLockToken was canceled while the ownership was being acquired,
                    // which means that at this point the shard isn't own already.
                    throw new RpcRerouteException("The shard isn't mapped to this node anymore.");
                }
                finally {
                    linkedCts.CancelAndDisposeSilently();
                }
            }
        }
    }
}
