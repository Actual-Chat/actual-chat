using ActualChat.Mesh;
using ActualLab.Diagnostics;

namespace ActualChat;

public sealed class ShardDispatcher : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardLocker;

    [field: AllowNull, MaybeNull]
    public ILogger Log => field ??= Services.LoggerFactory().CreateLogger(
        GetType(), $"({ShardDispatchers.ComposeFullKeyPrefix(ShardScheme, KeyPrefix)})");
    public ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    private IMeshLocks ShardLocks { get; }
    private MeshWatcher MeshWatcher => Host.MeshWatcher;
    private MeshNode ThisNode => Host.ThisNode;
    private StateFactory StateFactory => Host.StateFactory;
    private RunnableDispatcher RunnableDispatcher { get; } = new();

    public ShardDispatchers Host { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public string KeyPrefix { get; }
    public MeshLockOptions LockOptions { get; init; }
    public MutableState<FullState> State { get; }

    public ShardDispatcher(
        ShardDispatchers host,
        IMeshLocks shardLocks,
        ShardScheme shardScheme,
        string keyPrefix,
        CancellationTokenSource? stopTokenSource
        ) : base(stopTokenSource)
    {
        Host = host;
        Services = host.Services;
        ShardScheme = shardScheme;
        ShardLocks = shardLocks;
        LockOptions = shardLocks.LockOptions;
        KeyPrefix = keyPrefix;

        var meshState = MeshWatcher.State.LastNonErrorValue;
        var shardStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardState(this, i)).ToArray();
        State = StateFactory.NewMutable(
            initialValue: new FullState(this, meshState, shardStates),
            category: StateCategories.Get(GetType(), nameof(State)));
    }

    public IAsyncDisposable Use(string name, Func<LockState, CancellationToken, Task> func, RetryDelaySeq? retryDelays)
        => RunnableDispatcher.Use(new ShardRunnable(name, func) { RetryDelays = retryDelays });
    public IAsyncDisposable Use(string name, Func<LockState, CancellationToken, Task> func)
        => RunnableDispatcher.Use(new ShardRunnable(name, func));
    public IAsyncDisposable Use(ShardRunnable shardRunnable)
        => RunnableDispatcher.Use(shardRunnable);

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
                State.Value = new FullState(this, meshState, shardStates = nextShardStates);
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
                var lockState = new LockState(shardState, lockHolder);
                shardState.LockState.Value = lockState;
                RunnableDispatcher.Add(lockState);
                try {
                    await TaskExt.NeverEnding(lockToken).SilentAwait(false);
                }
                finally {
                    shardState.LockState.Value = null;
                    await RunnableDispatcher.Remove(lockState).ConfigureAwait(false);
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

    public sealed class FullState(
        ShardDispatcher dispatcher,
        MeshState meshState,
        IReadOnlyList<ShardState> shardStates)
    {
        public ShardDispatcher Dispatcher { get; } = dispatcher;
        public MeshState MeshState { get; } = meshState;
        public IReadOnlyList<ShardState> ShardStates { get; } = shardStates;
    }

    public sealed class ShardState : IAsyncDisposable
    {
        private readonly Task? _lockTask;
        private readonly CancellationTokenSource _cancelLockTokenSource;

        public ShardDispatcher Dispatcher { get; }
        public int ShardIndex { get; }
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public MutableState<LockState?> LockState { get; }
        public CancellationToken CancelLockToken { get; }
        public bool MustLock => _lockTask != null;
        public Task WhenDisposed { get; }

        internal ShardState(ShardDispatcher dispatcher, int shardIndex)
        {
            Dispatcher = dispatcher;
            ShardIndex = shardIndex;
            LockState = Dispatcher.Host.StateFactory.NewMutable<LockState?>(
                category: StateCategories.Get(GetType(), nameof(LockState)));
            _cancelLockTokenSource = Dispatcher.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            WhenDisposed = Task.CompletedTask;
            _cancelLockTokenSource.CancelAndDisposeSilently();
        }

        internal ShardState(ShardState prevState, bool mustLock)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            Dispatcher = prevState.Dispatcher;
            ShardIndex = prevState.ShardIndex;
            LockState = prevState.LockState;
            if (mustLock) {
                _cancelLockTokenSource = Dispatcher.StopToken.CreateLinkedTokenSource();
                CancelLockToken = _cancelLockTokenSource.Token;
                WhenDisposed = _lockTask = Dispatcher.LockAndUseShard(this, prevState);
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

    public sealed class LockState(ShardState shardState, MeshLockHolder lockHolder) : RunnableRunner
    {
        public ShardState ShardState { get; } = shardState;
        public MeshLockHolder LockHolder { get; } = lockHolder;

        // Handy shortcuts
        public ShardDispatcher Dispatcher { get; } = shardState.Dispatcher;
        public int ShardIndex { get; } = shardState.ShardIndex;
        public CancellationToken CancelLockToken { get; } = shardState.CancelLockToken;
        public CancellationToken LockToken { get; } = lockHolder.StopToken;
        public bool IsLockExpired => LockHolder.IsExpired;
    }
}
