using ActualLab.Diagnostics;
using ActualLab.Resilience;

namespace ActualChat.Sharding;

public sealed partial class ShardOwner : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardOwner;
    private static readonly RandomTimeSpan OwnershipWaitTimeout = TimeSpan.FromSeconds(1.5).ToRandom(0.2);
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

        var shardStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardState(this, i)).ToArray();
        State = StateFactory.NewMutable(
            initialValue: new OwnState(this, shardStates),
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

                    disposeTasks.Add(lockState.WhenDisposed);
                    nextShardStates[shardIndex] = new ShardState(lockState, mustLock);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                if (addedShards.Count > 0 || removedShards.Count > 0) {
                    State.Value = new OwnState(this, shardStates = nextShardStates);
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        lockedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));
                }

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
        IReadOnlyList<ShardState> shardStates)
    {
        public ShardOwner ShardOwner { get; } = shardOwner;
        public IReadOnlyList<ShardState> ShardStates { get; } = shardStates;
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
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public IState<ShardOwnership?> OwnershipState => MutableOwnershipState;
        public IState<Moment> InvalidateUntilState => MutableInvalidateUntilState;
        public CancellationToken CancelLockToken { get; }
        public bool MustLock => _lockTask != null;
        public Task WhenDisposed { get; }

        internal ShardState(ShardOwner shardOwner, int shardIndex)
        {
            ShardOwner = shardOwner;
            ShardIndex = shardIndex;
            var stateFactory = ShardOwner.Host.StateFactory;
            MutableOwnershipState = stateFactory.NewMutable<ShardOwnership?>(
                category: StateCategories.Get(GetType(), nameof(OwnershipState)));
            MutableInvalidateUntilState = stateFactory.NewMutable<Moment>(
                category: StateCategories.Get(GetType(), nameof(InvalidateUntilState)));
            _cancelLockTokenSource = ShardOwner.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            WhenDisposed = Task.CompletedTask;
            _cancelLockTokenSource.CancelAndDisposeSilently();
        }

        internal ShardState(ShardState prevState, bool mustLock)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            ShardOwner = prevState.ShardOwner;
            ShardIndex = prevState.ShardIndex;
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
            _cancelLockTokenSource.CancelAndDisposeSilently();
            return WhenDisposed.ToValueTask();
        }
    }
}
