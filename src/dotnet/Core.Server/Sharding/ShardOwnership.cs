namespace ActualChat.Sharding;

public sealed class ShardOwnership : RunnableRunner
{
    public ShardOwner.ShardState ShardState { get; }
    public MeshLockHolder LockHolder { get; }

    // Handy shortcuts
    public ShardOwner ShardOwner { get; }
    public int ShardIndex { get; }
    public CancellationToken CancelLockToken { get; }
    public CancellationToken LockToken { get; }
    public bool IsLockExpired => LockHolder.IsExpiredOnRenewal;
    public Moment AcquiredAt { get; }

    public ShardOwnership(ShardOwner.ShardState shardState, MeshLockHolder lockHolder)
    {
        ShardState = shardState;
        LockHolder = lockHolder;
        ShardOwner = shardState.ShardOwner;
        ShardIndex = shardState.ShardIndex;
        CancelLockToken = shardState.CancelLockToken;
        LockToken = lockHolder.StopToken;
        AcquiredAt = ShardOwner.Host.Clock.Now;
    }
}
