using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardLockerState(MeshState meshState, IReadOnlyList<ShardLockState> lockStates)
{
    public MeshState MeshState { get; } = meshState;
    public IReadOnlyList<ShardLockState> LockStates { get; } = lockStates;
}
