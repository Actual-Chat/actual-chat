namespace ActualChat.UI.Blazor.Components;

public abstract class ModalStepRef : IHasId<Symbol>
{
    private readonly AsyncTaskMethodBuilder<bool> _whenClosedSource = AsyncTaskMethodBuilderExt.New<bool>();

    public abstract Symbol Id { get; }
    public Task<bool> WhenClosed => _whenClosedSource.Task;

    protected void MarkClosedInternal(bool isModalClosing)
        => _whenClosedSource.TrySetResult(!isModalClosing);
}
