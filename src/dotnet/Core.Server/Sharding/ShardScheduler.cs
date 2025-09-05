using ActualChat.Mesh;
using ActualLab.Diagnostics;
using Google.Apis.Util;

namespace ActualChat;

public sealed class ShardScheduler : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.ShardLocker;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"({KeyPrefix}.{ShardScheme.Id.Value})");
    private ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    private IMeshLocks ShardLocks { get; }
    private MeshWatcher MeshWatcher => Owner.MeshWatcher;
    private MeshNode ThisNode => Owner.ThisNode;
    private StateFactory StateFactory => Owner.StateFactory;
    private RunnableScheduler SchedulerImpl { get; } = new();

    public ShardSchedulerSet Owner { get; }
    public IServiceProvider Services { get; }
    public ShardScheme ShardScheme { get; }
    public string KeyPrefix { get; }
    public MeshLockOptions LockOptions { get; init; }
    public MutableState<ShardSchedulerState> State { get; }

    public ShardScheduler(
        ShardSchedulerSet owner,
        IMeshLocks shardLocks,
        ShardScheme shardScheme,
        string keyPrefix,
        CancellationTokenSource? stopTokenSource
        ) : base(stopTokenSource)
    {
        Owner = owner;
        Services = owner.Services;
        ShardScheme = shardScheme;
        ShardLocks = shardLocks;
        LockOptions = shardLocks.LockOptions;
        KeyPrefix = keyPrefix.ThrowIfNullOrEmpty(nameof(keyPrefix));

        var meshState = MeshWatcher.State.LastNonErrorValue;
        var lockStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardState(this, i)).ToArray();
        State = StateFactory.NewMutable(
            initialValue: new ShardSchedulerState(this, meshState, lockStates),
            category: StateCategories.Get(GetType(), nameof(State)));
    }

    public IAsyncDisposable Schedule(Func<ShardLockState, CancellationToken, Task> func)
        => SchedulerImpl.Activate(new FuncShardRunner1(func));
    public IAsyncDisposable Schedule(Func<int, CancellationToken, Task> func)
        => SchedulerImpl.Activate(new FuncShardRunner2(func));

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lockedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        var disposeTasks = new List<Task>();
        var lockStates = State.Value.LockStates; // Initial value
        try {
            var changes = Owner.MeshWatcher.State.Computed.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
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
                var nextLockStates = new ShardState[lockStates.Count];
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var lockState = lockStates[shardIndex];
                    var mustLock = node == ThisNode;
                    if (lockState.MustLock == mustLock) {
                        nextLockStates[shardIndex] = lockState;
                        continue;
                    }

                    var nextShardState = new ShardState(lockState, mustLock);
                    nextLockStates[shardIndex] = nextShardState;
                    if (lockState != nextShardState)
                        disposeTasks.Add(lockState.WhenDisposed);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                State.Value = new ShardSchedulerState(this, meshState, lockStates = nextLockStates);
                if (addedShards.Count > 0 || removedShards.Count > 0)
                    Log.LogInformation("Shards @ {ThisNodeId}: {UsedShards} +[{AddedShards}] -[{RemovedShards}]",
                        ThisNode.Ref,
                        lockedShards.Format(), addedShards.ToDelimitedString(","), removedShards.ToDelimitedString(","));

                await Task.WhenAll(disposeTasks).SilentAwait(false);
                disposeTasks.Clear();
            }
        }
        finally {
            await Task.WhenAll(lockStates.Select(x => x.DisposeAsync().AsTask())).SilentAwait(false);
        }
    }

    // Private methods

    private async Task LockShard(ShardState shardState, ShardState prevShardState)
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
                var lockProcessor = new ShardLockState(shardState, lockHolder);
                shardState.LockState.Value = lockProcessor;
                SchedulerImpl.Add(lockProcessor);
                try {
                    await Task.Delay(System.Threading.Timeout.Infinite, lockToken).SilentAwait(false);
                }
                finally {
                    shardState.LockState.Value = null;
                    await SchedulerImpl.Remove(lockProcessor).ConfigureAwait(false);
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

    public sealed class ShardState : IAsyncDisposable
    {
        private readonly Task? _lockTask;
        private readonly CancellationTokenSource _cancelLockTokenSource;

        public ShardScheduler Scheduler { get; }
        public int ShardIndex { get; }
        public MutableState<ShardLockState?> LockState { get; }
        public CancellationToken CancelLockToken { get; }
        public bool MustLock => _lockTask != null;
        public Task WhenDisposed { get; }

        internal ShardState(ShardScheduler scheduler, int shardIndex)
        {
            Scheduler = scheduler;
            ShardIndex = shardIndex;
            LockState = Scheduler.Owner.StateFactory.NewMutable<ShardLockState?>(
                category: StateCategories.Get(GetType(), nameof(LockState)));
            _cancelLockTokenSource = Scheduler.StopToken.CreateLinkedTokenSource();
            CancelLockToken = _cancelLockTokenSource.Token;
            WhenDisposed = Task.CompletedTask;
            _cancelLockTokenSource.CancelAndDisposeSilently();
        }

        internal ShardState(ShardState prevState, bool mustLock)
        {
            prevState._cancelLockTokenSource.CancelAndDisposeSilently();
            Scheduler = prevState.Scheduler;
            ShardIndex = prevState.ShardIndex;
            LockState = prevState.LockState;
            if (mustLock) {
                _cancelLockTokenSource = Scheduler.StopToken.CreateLinkedTokenSource();
                CancelLockToken = _cancelLockTokenSource.Token;
                WhenDisposed = _lockTask = Scheduler.LockShard(this, prevState);
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

    public sealed class ShardLockState(ShardState shardState, MeshLockHolder lockHolder) : RunnableRunner
    {
        public ShardState ShardState { get; } = shardState;
        public MeshLockHolder LockHolder { get; } = lockHolder;

        // Handy shortcuts
        public ShardScheduler Scheduler { get; } = shardState.Scheduler;
        public int ShardIndex { get; } = shardState.ShardIndex;
        public CancellationToken CancelLockToken { get; } = shardState.CancelLockToken;
        public CancellationToken LockToken { get; } = lockHolder.StopToken;
        public bool IsLockExpired => LockHolder.IsExpired;
    }

    private sealed record FuncShardRunner1(Func<ShardLockState, CancellationToken, Task> Func) : IRunnable
    {
        public Task Start(IRunnableRunner runner, CancellationToken cancellationToken)
            => Func.Invoke((ShardLockState)runner, cancellationToken);
    }

    private sealed record FuncShardRunner2(Func<int, CancellationToken, Task> Func) : IRunnable
    {
        public Task Start(IRunnableRunner runner, CancellationToken cancellationToken)
            => Func.Invoke(((ShardLockState)runner).ShardIndex, cancellationToken);
    }
}
