namespace ActualChat.Sharding;

public sealed class ShardLease : RunnableRunner
{
    public ShardBroker.ShardState ShardState { get; }
    public MeshLockHolder LockHolder { get; }

    // Handy shortcuts
    public ShardBroker ShardBroker { get; }
    public int ShardIndex { get; }
    public CancellationToken CancelLockToken { get; }
    public CancellationToken LockToken { get; }
    public bool IsLockExpired => LockHolder.IsExpired;
    public Moment AcquiredAt { get; }

    public ShardLease(ShardBroker.ShardState shardState, MeshLockHolder lockHolder)
    {
        ShardState = shardState;
        LockHolder = lockHolder;
        ShardBroker = shardState.ShardBroker;
        ShardIndex = shardState.ShardIndex;
        CancelLockToken = shardState.CancelLockToken;
        LockToken = lockHolder.StopToken;
        AcquiredAt = ShardBroker.Host.Clock.Now;
    }
}
