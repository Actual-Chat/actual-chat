using ActualChat.Mesh;

namespace ActualChat;

public sealed class ShardProcessor(ShardLockState lockState, MeshLockHolder lockHolder) : RunnableRunner
{
    public ShardLocker ShardLocker { get; } = lockState.ShardLocker;
    public int ShardIndex { get; } = lockState.ShardIndex;
    public ShardLockState LockState { get; } = lockState;
    public MeshLockHolder LockHolder { get; } = lockHolder;
    public CancellationToken CancelLockToken { get; } = lockState.CancelLockToken;
    public CancellationToken LockToken { get; } = lockHolder.StopToken;

    // Nested types

    public sealed record Runner(Func<ShardProcessor, CancellationToken, Task> Func) : IRunnable
    {
        public Task Start(IRunnableRunner runner, CancellationToken cancellationToken)
            => Func.Invoke((ShardProcessor)runner, cancellationToken);
    }

    public sealed record LegacyRunner(Func<int, CancellationToken, Task> Func) : IRunnable
    {
        public Task Start(IRunnableRunner runner, CancellationToken cancellationToken)
            => Func.Invoke(((ShardProcessor)runner).ShardIndex, cancellationToken);
    }
}
