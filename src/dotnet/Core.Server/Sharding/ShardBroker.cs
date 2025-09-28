using ActualLab.Diagnostics;
using ActualLab.Resilience;

namespace ActualChat.Sharding;

public sealed class ShardBroker : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardBroker;

    [field: AllowNull, MaybeNull]
    internal ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"@{ShardScheme.Name}");
    private ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    private IMeshLocks ShardLocks { get; }
    private MeshWatcher MeshWatcher => Host.MeshWatcher;
    private MeshNode ThisNode => Host.ThisNode;
    private StateFactory StateFactory => Host.StateFactory;
    private MomentClock Clock => Host.Clock;
    private RunnableDispatcher Dispatcher { get; } = new();

    public ShardBrokers Host { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public MeshLockOptions LockOptions { get; init; }
    public ShardLeaseTracker ShardLeaseTracker { get; init; }
    public MutableState<BrokerState> State { get; }
    public IReadOnlyList<ShardState> ShardStates => State.Value.ShardStates;

    public ShardBroker(
        ShardBrokers host,
        IMeshLocks shardLocks,
        ShardScheme shardScheme,
        CancellationTokenSource stopTokenSource
        ) : base(stopTokenSource)
    {
        Host = host;
        Services = host.Services;
        ShardScheme = shardScheme;
        ShardLocks = shardLocks;
        LockOptions = shardLocks.LockOptions;
        ShardLeaseTracker = new ShardLeaseTracker(this);

        var meshState = MeshWatcher.State.LastNonErrorValue;
        var shardStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardState(this, i)).ToArray();
        State = StateFactory.NewMutable(
            initialValue: new BrokerState(this, meshState, shardStates),
            category: StateCategories.Get(GetType(), nameof(State)));
    }

    public IAsyncDisposable Use(string name, Func<ShardLease, CancellationToken, Task> func, IRetryPolicy? retryPolicy)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock) { RetryPolicy = retryPolicy });
    public IAsyncDisposable Use(string name, Func<ShardLease, CancellationToken, Task> func)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock));
    public IAsyncDisposable Use(string name, Func<int, CancellationToken, Task> func, IRetryPolicy? retryPolicy)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock) { RetryPolicy = retryPolicy });
    public IAsyncDisposable Use(string name, Func<int, CancellationToken, Task> func)
        => Dispatcher.Use(new ShardRunnable(name, func, Clock));
    public IAsyncDisposable Use(ShardRunnable shardRunnable)
        => Dispatcher.Use(shardRunnable);

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lockedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        var disposeTasks = new List<Task>();
        var shardStates = State.Value.ShardStates; // Initial value
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

                    var nextShardState = new ShardState(lockState, mustLock);
                    nextShardStates[shardIndex] = nextShardState;
                    if (lockState != nextShardState)
                        disposeTasks.Add(lockState.WhenDisposed);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                State.Value = new BrokerState(this, meshState, shardStates = nextShardStates);
                if (addedShards.Count > 0 || removedShards.Count > 0)
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        lockedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));

                await Task.WhenAll(disposeTasks).SilentAwait(false);
                disposeTasks.Clear();
            }
        }
        finally {
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
            var lockHolder = await ShardLocks.Lock(shardIndex.Format(), "", cancelLockToken).ConfigureAwait(false);
            await using var _1 = lockHolder.ConfigureAwait(false);
            var lockToken = lockHolder.StopToken;
            DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);

            // Create the processor
            if (!lockToken.IsCancellationRequested) {
                var shardLease = new ShardLease(shardState, lockHolder);
                shardState.LeaseState.Value = shardLease;
                Dispatcher.Add(shardLease);
                try {
                    await TaskExt.NeverEnding(lockToken).SilentAwait(false);
                }
                finally {
                    shardState.LeaseState.Value = null;
                    await Dispatcher.Remove(shardLease).ConfigureAwait(false);
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

    public sealed class BrokerState(
        ShardBroker shardBroker,
        MeshState meshState,
        IReadOnlyList<ShardState> shardStates)
    {
        public ShardBroker ShardBroker { get; } = shardBroker;
        public MeshState MeshState { get; } = meshState;
        public IReadOnlyList<ShardState> ShardStates { get; } = shardStates;
        public Moment CreatedAt { get; } = shardBroker.Host.Clock.Now;
        public TimeSpan Age => ShardBroker.Host.Clock.Now - CreatedAt;
    }

    public sealed class ShardState : IAsyncDisposable
    {
        private readonly Task? _lockTask;
        private readonly CancellationTokenSource _cancelLockTokenSource;

        public ShardBroker ShardBroker { get; }
        public int ShardIndex { get; }
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public MutableState<ShardLease?> LeaseState { get; }
        public CancellationToken CancelLockToken { get; }
        public bool MustLock => _lockTask != null;
        public Task WhenDisposed { get; }

        internal ShardState(ShardBroker shardBroker, int shardIndex)
        {
            ShardBroker = shardBroker;
            ShardIndex = shardIndex;
            LeaseState = ShardBroker.Host.StateFactory.NewMutable<ShardLease?>(
                category: StateCategories.Get(GetType(), nameof(LeaseState)));
            _cancelLockTokenSource = ShardBroker.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            WhenDisposed = Task.CompletedTask;
            _cancelLockTokenSource.CancelAndDisposeSilently();
        }

        internal ShardState(ShardState prevState, bool mustLock)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            ShardBroker = prevState.ShardBroker;
            ShardIndex = prevState.ShardIndex;
            LeaseState = prevState.LeaseState;
            if (mustLock) {
                _cancelLockTokenSource = ShardBroker.StopToken.CreateLinkedTokenSource();
                CancelLockToken = _cancelLockTokenSource.Token;
                WhenDisposed = _lockTask = ShardBroker.LockAndUseShard(this, prevState);
            }
            else {
                _cancelLockTokenSource = prevState._cancelLockTokenSource;
                CancelLockToken = prevState.CancelLockToken;
                WhenDisposed = prevState.WhenDisposed;
            }
        }

        public ValueTask DisposeAsync()
        {
            _cancelLockTokenSource.CancelAndDisposeSilently();
            return WhenDisposed.ToValueTask();
        }
    }
}
