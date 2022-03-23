using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public class InteractionUI
{
    private readonly IModalService _modalService;
    private AsyncLock _asyncLock = new AsyncLock(ReentryMode.CheckedFail);
    private bool interacted;

    public InteractionUI(IModalService modalService)
        => _modalService = modalService;

    public async Task RequestInteraction()
    {
        if (interacted)
            return;
        using var _ = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        if (interacted)
            return;
        var modalReference = _modalService.Show<InteractionRequest>(
            null,
            new ModalOptions { HideHeader = true, Class = "blazored-modal-p0"}
            //z-102 bg-white flex flex-column border rounded
        );
        await modalReference.Result.ConfigureAwait(false);
        interacted = true;
    }
}
