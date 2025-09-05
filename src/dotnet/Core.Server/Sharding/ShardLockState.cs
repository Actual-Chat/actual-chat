namespace ActualChat;

public sealed class ShardLockState : IAsyncDisposable
{
    private CancellationTokenSource CancelLockTokenSource { get; }

    public ShardLocker ShardLocker { get; }
    public int ShardIndex { get; }
    public MutableState<ShardProcessor?> ProcessorState { get; }
    public CancellationToken CancelLockToken { get; }
    public Task? LockTask { get; }
    public bool MustLock => LockTask != null;
    public Task WhenDisposed { get; }

    internal ShardLockState(ShardLocker shardLocker, int shardIndex)
    {
        ShardLocker = shardLocker;
        ShardIndex = shardIndex;
        ProcessorState = ShardLocker.ShardLockers.StateFactory.NewMutable<ShardProcessor?>(
            category: StateCategories.Get(GetType(), nameof(ProcessorState)));
        CancelLockTokenSource = ShardLocker.StopToken.CreateLinkedTokenSource();
        CancelLockToken = CancelLockTokenSource.Token;
        CancelLockTokenSource.CancelAndDisposeSilently();
        WhenDisposed = Task.CompletedTask;
    }

    internal ShardLockState(ShardLockState prevState, bool mustLock)
    {
        prevState.CancelLockTokenSource.CancelAndDisposeSilently();
        ShardLocker = prevState.ShardLocker;
        ShardIndex = prevState.ShardIndex;
        ProcessorState = prevState.ProcessorState;
        if (mustLock) {
            CancelLockTokenSource = ShardLocker.StopToken.CreateLinkedTokenSource();
            CancelLockToken = CancelLockTokenSource.Token;
            LockTask = ShardLocker.LockShard(this, prevState);
            WhenDisposed = LockTask;
        }
        else {
            CancelLockTokenSource = prevState.CancelLockTokenSource;
            CancelLockToken = prevState.CancelLockToken;
            WhenDisposed = prevState.WhenDisposed;
        }
    }

    public ValueTask DisposeAsync()
    {
        CancelLockTokenSource.CancelAndDisposeSilently();
        return WhenDisposed.ToValueTask();
    }
}
