using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardSchedulerState(
    ShardScheduler scheduler,
    MeshState meshState,
    IReadOnlyList<ShardScheduler.ShardState> lockStates)
{
    public ShardScheduler Scheduler { get; } = scheduler;
    public MeshState MeshState { get; } = meshState;
    public IReadOnlyList<ShardScheduler.ShardState> LockStates { get; } = lockStates;
}
