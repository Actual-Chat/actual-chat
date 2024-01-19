namespace ActualChat.Mesh;

public class MeshStateWatcher : WorkerBase
{
    private volatile AsyncState<MeshInfo> _state = new(MeshInfo.Empty, true);

    public AsyncState<MeshInfo> State => _state;

    protected override Task OnRun(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
