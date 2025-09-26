using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardRunner(ShardBroker.ShardState shardState, MeshLockHolder lockHolder) : RunnableRunner
{
    public ShardBroker.ShardState ShardState { get; } = shardState;
    public MeshLockHolder LockHolder { get; } = lockHolder;

    // Handy shortcuts
    public ShardBroker ShardBroker { get; } = shardState.ShardBroker;
    public int ShardIndex { get; } = shardState.ShardIndex;
    public CancellationToken CancelLockToken { get; } = shardState.CancelLockToken;
    public CancellationToken LockToken { get; } = lockHolder.StopToken;
    public bool IsLockExpired => LockHolder.IsExpired;
}
