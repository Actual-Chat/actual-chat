using ActualChat.Mesh;
using ActualLab.Diagnostics;
using Google.Apis.Util;

namespace ActualChat;

public sealed class ShardLocker : WorkerBase
{
    private static bool DebugMode => Constants.DebugMode.ShardLocker;

    [field: AllowNull, MaybeNull]
    internal ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"({KeyPrefix}.{ShardScheme.Id.Value})");
    internal ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    private IMeshLocks ShardLocks { get; }
    private MeshWatcher MeshWatcher => ShardLockers.MeshWatcher;
    private MeshNode ThisNode => ShardLockers.ThisNode;
    private StateFactory StateFactory => ShardLockers.StateFactory;
    private RunnableScheduler Scheduler { get; } = new();

    public IServiceProvider Services { get; }
    public ShardLockers ShardLockers { get; }
    public ShardScheme ShardScheme { get; }
    public string KeyPrefix { get; }
    public MeshLockOptions LockOptions { get; init; }
    public MutableState<ShardLockerState> State { get; }

    public ShardLocker(
        ShardLockers shardLockers,
        IMeshLocks shardLocks,
        ShardScheme shardScheme,
        string keyPrefix,
        CancellationTokenSource? stopTokenSource
        ) : base(stopTokenSource)
    {
        Services = shardLockers.Services;
        ShardLockers = shardLockers;
        ShardScheme = shardScheme;
        ShardLocks = shardLocks;
        LockOptions = shardLocks.LockOptions;
        KeyPrefix = keyPrefix.ThrowIfNullOrEmpty(nameof(keyPrefix));

        var meshState = MeshWatcher.State.LastNonErrorValue;
        var lockStates = Enumerable.Range(0, shardScheme.ShardCount).Select(i => new ShardLockState(this, i)).ToArray();
        State = StateFactory.NewMutable(
            initialValue: new ShardLockerState(meshState, lockStates),
            category: StateCategories.Get(GetType(), nameof(State)));
    }

    public IAsyncDisposable Schedule(Func<ShardProcessor, CancellationToken, Task> func)
        => Scheduler.Activate(new ShardProcessor.Runner(func));
    public IAsyncDisposable Schedule(Func<int, CancellationToken, Task> func)
        => Scheduler.Activate(new ShardProcessor.LegacyRunner(func));

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lockedShards = new BitArray(ShardScheme.ShardCount);
        var addedShards = new List<int>();
        var removedShards = new List<int>();
        var disposeTasks = new List<Task>();
        var lockStates = State.Value.LockStates; // Initial value
        try {
            var changes = ShardLockers.MeshWatcher.State.Computed.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
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
                var nextLockStates = new ShardLockState[lockStates.Count];
                foreach (var shardIndex in ShardScheme.ShardIndexes) {
                    var nodeIndex = nodeIndexes[shardIndex];
                    var node = nodeIndex.HasValue ? nodes[nodeIndex.GetValueOrDefault()] : null;
                    var lockState = lockStates[shardIndex];
                    var mustLock = node == ThisNode;
                    if (lockState.MustLock == mustLock) {
                        nextLockStates[shardIndex] = lockState;
                        continue;
                    }

                    var nextShardState = new ShardLockState(lockState, mustLock);
                    nextLockStates[shardIndex] = nextShardState;
                    if (lockState != nextShardState)
                        disposeTasks.Add(lockState.WhenDisposed);
                    (mustLock ? addedShards : removedShards).Add(shardIndex);
                    lockedShards[shardIndex] = mustLock;
                }
                State.Value = new ShardLockerState(meshState, lockStates = nextLockStates);
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

    // Internal and private methods

    internal async Task LockShard(ShardLockState lockState, ShardLockState prevLockState)
    {
        // We must make sure we don't run LockShard in parallel with the previous one.
        // We always cancel the previous one, but there is no guarantee that it will stop immediately.
        await prevLockState.WhenDisposed.SilentAwait(false);

        var shardIndex = lockState.ShardIndex;
        var cancelLockToken = lockState.CancelLockToken;
        for (var index = 1; !cancelLockToken.IsCancellationRequested; index++) {
            // Acquire the lock
            DebugLog?.LogDebug("Shard #{ShardIndex}: ?++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);
            var lockHolder = await ShardLocks.Lock(shardIndex.Format(), "", cancelLockToken).ConfigureAwait(false);
            await using var _1 = lockHolder.ConfigureAwait(false);
            var lockToken = lockHolder.StopToken;
            DebugLog?.LogDebug("Shard #{ShardIndex}: ++ {ThisNodeId} (#{Index})", shardIndex, ThisNode.Ref, index);

            // Create the worker
            if (!lockToken.IsCancellationRequested) {
                var worker = new ShardProcessor(lockState, lockHolder);
                lockState.ProcessorState.Value = worker;
                Scheduler.Add(worker);
                try {
                    await Task.Delay(System.Threading.Timeout.Infinite, lockToken).SilentAwait(false);
                }
                finally {
                    lockState.ProcessorState.Value = null;
                    await Scheduler.Remove(worker).ConfigureAwait(false);
                }
            }

            if (cancelLockToken.IsCancellationRequested)
                break;

            Log.LogWarning(
                "Shard #{ShardIndex}: -- {ThisNodeId} - lost the lock (#{Index})",
                shardIndex, ThisNode.Ref, index);
        }
    }

}
