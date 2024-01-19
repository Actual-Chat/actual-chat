namespace ActualChat.Mesh;

public sealed class MeshWatcher(IServiceProvider services) : WorkerBase
{
    private volatile AsyncState<MeshState> _state = new(MeshState.Empty, true);

    private IMeshLocks<MeshState> MeshLocks { get; } = services.GetRequiredService<IMeshLocks<MeshState>>();

    public MeshNode ThisNode { get; } = services.GetRequiredService<MeshNode>();
    public AsyncState<MeshState> State => _state;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        return;
        var whenLockedTcs = new TaskCompletionSource();
        _ = KeepLocked(whenLockedTcs, cancellationToken);
        await whenLockedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var state = _state.Value;
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var nodes = await ListNodes(cancellationToken).ConfigureAwait(false);
                var isSameState = nodes.Length == state.Nodes.Length
                    && nodes.Zip(state.Nodes).All(x => x.First == x.Second);
                if (!isSameState) {
                    state = new MeshState(nodes);
                    _state = _state.SetNext(state);
                }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // Intended: we keep the lock unless cancellationToken is cancelled
            }
        }
    }

    protected override Task OnStop()
    {
        _state.SetFinal(StopToken);
        return Task.CompletedTask;
    }

    private async Task<ImmutableArray<MeshNode>> ListNodes(CancellationToken cancellationToken)
    {
        var keys = await MeshLocks.ListKeys("", cancellationToken).ConfigureAwait(false);
        return keys.Order(StringComparer.Ordinal).Select(MeshNode.Parse).ToImmutableArray();
    }

    private async Task KeepLocked(TaskCompletionSource whenLockedTcs, CancellationToken cancellationToken)
    {
        var key = ThisNode.ToString();
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var holder = await MeshLocks
                    .Lock(key, "", cancellationToken)
                    .ConfigureAwait(false);
                await using var _ = holder.ConfigureAwait(false);
                whenLockedTcs.TrySetResult();
                using var lts = cancellationToken.LinkWith(holder.StopToken);
                await ActualLab.Async.TaskExt.NeverEndingTask.WaitAsync(lts.Token).SilentAwait();
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // Intended: we keep the lock unless cancellationToken is cancelled
            }
        }
    }
}
