using Blazored.Modal;
using Blazored.Modal.Services;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public class UserInteractionUI
{
    private readonly IModalService _modalService;
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private readonly TaskSource<Unit> _whenInteractionHappenedSource;

    public Task WhenInteractionHappened => _whenInteractionHappenedSource.Task;

    public UserInteractionUI(IModalService modalService)
    {
        _modalService = modalService;
        _whenInteractionHappenedSource = TaskSource.New<Unit>(true);
    }

    public void MarkInteractionHappened()
        => _whenInteractionHappenedSource.TrySetResult(default);

    public async Task RequestInteraction()
    {
        if (WhenInteractionHappened.IsCompleted)
            return;
        using var _ = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        if (WhenInteractionHappened.IsCompleted)
            return;
        var modal = _modalService.Show<UserInteractionRequestModal>(
            null,
            new ModalOptions { HideHeader = true, Class = "blazored-modal-p0 bg-secondary shadow-lg"}
            //z-102 bg-white flex flex-column border rounded
        );
        await modal.Result.ConfigureAwait(false);
        MarkInteractionHappened();
    }
}
