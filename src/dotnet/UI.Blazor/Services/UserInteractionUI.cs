using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public class UserInteractionUI
{
    private readonly ModalUI _modalUI;
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private readonly TaskSource<Unit> _whenInteractionHappenedSource;

    public Task WhenInteractionHappened => _whenInteractionHappenedSource.Task;

    public UserInteractionUI(ModalUI modalUI)
    {
        _modalUI = modalUI;
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
        var model = new UserInteractionRequestModal.Model(operation.NullIfEmpty() ?? "audio playback or capture");
        var modal = _modalUI.Show(model);
        await modal.Result.ConfigureAwait(false);
        MarkInteractionHappened();
    }
}
