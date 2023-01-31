using Blazored.Modal.Services;

namespace Blazored.Modal;

public class ModalRef : IModalRef
{
    private readonly TaskSource<Unit> _whenClosedSource = TaskSource.New<Unit>(true);
    private readonly ModalService _modalService;

    internal Guid Id { get; }
    internal RenderFragment ModalInstance { get; }
    internal BlazoredModalInstance? ModalInstanceRef { get; set; }

    public event EventHandler<ModalCloseRequestEventArgs>? ModalCloseRequest;

    public ModalRef(Guid modalInstanceId, RenderFragment modalInstance, ModalService modalService)
    {
        Id = modalInstanceId;
        ModalInstance = modalInstance;
        _modalService = modalService;
    }

    public void Close()
        => _modalService.Close(this);

    public Task WhenClosed => _whenClosedSource.Task;

    internal void Dismiss()
        => _ = _whenClosedSource.TrySetResult(default);

    internal bool RaiseModalCloseRequest()
    {
        var args = new ModalCloseRequestEventArgs();
        ModalCloseRequest?.Invoke(this, args);
        return args.Handled;
    }
}

public class ModalCloseRequestEventArgs : EventArgs
{
    public bool Handled { get; set; }
}
