using ActualLab.Diagnostics;
using ActualLab.Fusion.Internal;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Sharding;

public sealed class ShardOwner : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardOwners;
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

    public ShardOwnershipStatus GetShardOwnershipStatus<T>(T shardKey, bool addDependency = true)
        => GetShardState(shardKey).GetOwnershipStatus(addDependency);

    public Task<ShardOwnership> RequireOwnership<T>(T shardKey, CancellationToken cancellationToken)
        => GetShardState(shardKey).RequireOwnership(addDependency: true, cancellationToken);
    public Task<ShardOwnership> RequireOwnership<T>(T shardKey, bool addDependency, CancellationToken cancellationToken)
        => GetShardState(shardKey).RequireOwnership(addDependency, cancellationToken);

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var shardStates = State.Value.ShardStates; // Initial value
        var shardMap = new ShardMap(ShardScheme, []);
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

                shardMap = meshState.GetShardMap(ShardScheme);
                await Update(shardMap, false).SilentAwait(false);
            }
        }
        finally {
            await Update(shardMap, true).SilentAwait(false);
        }
        return;

        // ReSharper disable once VariableHidesOuterVariable
        Task Update(ShardMap shardMap, bool isFinal) {
            if (isFinal)
                version = -1;

            var addedShards = new List<int>();
            var removedShards = new List<int>();
            var lockedShards = new BitArray(ShardScheme.ShardCount);
            var waitList = new List<Task>();
            var nodes = shardMap.Nodes;
            var nodeIndexes = shardMap.NodeIndexes;
            var nextShardStates = new ShardState[shardStates.Count];
            foreach (var shardIndex in ShardScheme.ShardIndexes) {
                var nodeIndex = nodeIndexes[shardIndex];
                var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                var shardState = shardStates[shardIndex];
                var mustLock = !isFinal && node == ThisNode;
                if (shardState.MustLock == mustLock) {
                    nextShardStates[shardIndex] = shardState;
                    continue;
                }

                waitList.Add(shardState.WhenLockAndUseRunning);
                nextShardStates[shardIndex] = new ShardState(shardState, mustLock, version);
                (mustLock ? addedShards : removedShards).Add(shardIndex);
                lockedShards[shardIndex] = mustLock;
            }
            if (addedShards.Count > 0 || removedShards.Count > 0 || isFinal) {
                // ReSharper disable once HeapView.CanAvoidClosure
                State.Value = new OwnState(this, shardStates = nextShardStates, version);
                Log.LogInformation("Shards @ {ThisNodeId}, v{Version}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                    ThisNode.Ref,
                    isFinal ? $"{version.Format()} (final)" : version.Format(),
                    lockedShards.Format(),
                    addedShards.ToDelimitedString(","),
                    removedShards.ToDelimitedString(","));
                if (!isFinal)
                    version++;
            }
            return Task.WhenAll(waitList);
        }
    }

    // Private methods

    private async Task LockAndUseShard(ShardState shardState, ShardState prevShardState)
    {
        // We must make sure we don't run LockShard in parallel with the previous one.
        // We always cancel the previous one, but there is no guarantee that it will stop immediately.
        if (prevShardState.WhenLockAndUseRunning is { } prevLockAndUseRunning)
            await prevLockAndUseRunning.SilentAwait(false);

        var shardIndex = shardState.ShardIndex;
        var cancelLockToken = shardState.CancelLockToken;
        for (var index = 1; !cancelLockToken.IsCancellationRequested; index++) {
            // Acquire the lock
            DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);
            var lockHolder = await OwnershipLocks.Lock(shardIndex.Format(), cancelLockToken).ConfigureAwait(false);
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
        public bool IsFinal => Version < 0;
    }

    public sealed class ShardState
    {
        private readonly TaskCompletionSource<ShardState> _nextState;
        private readonly CancellationTokenSource _cancelLockTokenSource;

        internal readonly Lock MutableStateChangeLock = new();
        internal readonly MutableState<ShardOwnership?> MutableOwnershipState;
        internal readonly MutableState<Moment> MutableInvalidateUntilState;

        public ShardOwner ShardOwner { get; }
        public int ShardIndex { get; }
        public long ShardOwnerStateVersion { get; }
        public CancellationToken CancelLockToken { get; }
        public bool MustLock { get; }
        public IState<ShardOwnership?> OwnershipState => MutableOwnershipState;
        public IState<Moment> InvalidateUntilState => MutableInvalidateUntilState;
        public Task WhenLockAndUseRunning { get; }
        public Task<ShardState> WhenChanged => _nextState.Task;

        internal ShardState(ShardOwner shardOwner, int shardIndex, int shardOwnerStateVersion)
        {
            ShardOwner = shardOwner;
            ShardIndex = shardIndex;
            ShardOwnerStateVersion = shardOwnerStateVersion;

            _nextState = TaskCompletionSourceExt.New<ShardState>();
            _cancelLockTokenSource = ShardOwner.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            _cancelLockTokenSource.CancelAndDisposeSilently(); // ~ MustLock = false

            var stateFactory = ShardOwner.Host.StateFactory;
            MutableOwnershipState = stateFactory.NewMutable<ShardOwnership?>(
                category: StateCategories.Get(GetType(), nameof(OwnershipState)));
            MutableInvalidateUntilState = stateFactory.NewMutable<Moment>(
                category: StateCategories.Get(GetType(), nameof(InvalidateUntilState)));
            WhenLockAndUseRunning = Task.CompletedTask;
        }

        internal ShardState(ShardState prevState, bool mustLock, int shardOwnerStateVersion)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            _nextState = TaskCompletionSourceExt.New<ShardState>();
            ShardOwner = prevState.ShardOwner;
            ShardIndex = prevState.ShardIndex;
            ShardOwnerStateVersion = shardOwnerStateVersion;
            MutableOwnershipState = prevState.MutableOwnershipState;
            MutableInvalidateUntilState = prevState.MutableInvalidateUntilState;
            MustLock = mustLock;
            if (mustLock) {
                _cancelLockTokenSource = ShardOwner.StopToken.CreateLinkedTokenSource();
                CancelLockToken = _cancelLockTokenSource.Token;
                WhenLockAndUseRunning = ShardOwner.LockAndUseShard(this, prevState);
            }
            else {
                _cancelLockTokenSource = prevState._cancelLockTokenSource;
                CancelLockToken = prevState.CancelLockToken;
                WhenLockAndUseRunning = prevState.WhenLockAndUseRunning;
            }
            prevState._nextState.SetResult(this);
        }

        public ShardOwnershipStatus GetOwnershipStatus(bool addDependency = true)
        {
            var cCurrent = addDependency ? Computed.Current : null;
            var cOwnershipState = OwnershipState.Computed;
            if (cOwnershipState.Value is not null) {
                // ShardOwnershipStatus.LockedByThisNode
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return ShardOwnershipStatus.LockedByThisNode;
            }

            if (MustLock) {
                // ShardOwnershipStatus.MappedToThisNode
                if (cCurrent is not null)
                    ComputedImpl.AddDependency(cCurrent, cOwnershipState);
                return ShardOwnershipStatus.MappedToThisNode;
            }

            // ShardOwnershipStatus.MappedToOtherNode
            if (cCurrent is not null) {
                var cShardOwnerState = ShardOwner.State.Computed;
                var shardOwnerState = cShardOwnerState.Value;
                if (shardOwnerState.Version != ShardOwnerStateVersion)
                    cCurrent.Invalidate(immediately: true); // ShardOwner.State has changed
                else if (!shardOwnerState.IsFinal)
                    ComputedImpl.AddDependency(cCurrent, cShardOwnerState);
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
                var cShardOwnerState = ShardOwner.State.Computed;
                var shardOwnerState = cShardOwnerState.Value;
                if (shardOwnerState.Version != ShardOwnerStateVersion)
                    cCurrent.Invalidate(immediately: true); // ShardOwner.State has changed
                else if (!shardOwnerState.IsFinal)
                    ComputedImpl.AddDependency(cCurrent, cShardOwnerState);
            }
            throw RpcRerouteException.MustReroute("the shard isn't mapped to this node");

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
                    throw RpcRerouteException.MustReroute("the shard isn't mapped to this node anymore");
                }
                finally {
                    linkedCts.CancelAndDisposeSilently();
                }
            }
        }
    }
}
