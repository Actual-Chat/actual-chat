using Blazored.Modal.Services;

namespace Blazored.Modal;

public class ModalReference : IModalReference
{
    private readonly TaskCompletionSource _resultCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ModalService _modalService;

    internal Guid Id { get; }
    internal RenderFragment ModalInstance { get; }
    internal BlazoredModalInstance? ModalInstanceRef { get; set; }

    public ModalReference(Guid modalInstanceId, RenderFragment modalInstance, ModalService modalService)
    {
        Id = modalInstanceId;
        ModalInstance = modalInstance;
        _modalService = modalService;
    }

    public void Close()
        => _modalService.Close(this);

    public Task WhenClosed => _resultCompletion.Task;

    internal void Dismiss()
        => _ = _resultCompletion.TrySetResult();
}
