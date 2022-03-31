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

    public async Task RequestInteraction(string operation = "")
    {
        if (WhenInteractionHappened.IsCompleted)
            return;
        using var _ = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        if (WhenInteractionHappened.IsCompleted)
            return;
        var parameters = new ModalParameters();
        if (!operation.IsNullOrEmpty())
            parameters.Add("Operation", operation);
        var modal = _modalService.Show<UserInteractionRequestModal>(
            null,
            parameters,
            new ModalOptions {
                HideHeader = true,
                Class = "modal",
            }
        );
        await modal.Result.ConfigureAwait(false);
        MarkInteractionHappened();
    }
}
