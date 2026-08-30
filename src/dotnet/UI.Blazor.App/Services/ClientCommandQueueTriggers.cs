namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Publishes <see cref="ClientCommandQueue"/> changes into the compute graph:
/// its methods carry no data, they exist to be invalidated and depended upon.
/// </summary>
public class ClientCommandQueueTriggers : IComputeService, IHasDisposeStatus
{
    [ComputeMethod]
    public virtual Task<Unit> OnChanged(string partitionKey)
        => ActualLab.Async.TaskExt.UnitTask;

    [ComputeMethod]
    public virtual Task<Unit> OnAnyChanged()
        => ActualLab.Async.TaskExt.UnitTask;

    public bool IsDisposed { get; set; }
}
