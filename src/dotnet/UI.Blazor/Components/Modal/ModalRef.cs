using ActualChat.UI.Blazor.Components.Internal;

namespace ActualChat.UI.Blazor.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ModalRef : IHasId<Symbol>, IModalRefImpl
{
    private readonly TaskSource<Unit> _whenShownSource = TaskSource.New<Unit>(true);
    private readonly TaskSource<Unit> _whenClosedSource = TaskSource.New<Unit>(true);
    private RenderFragment _view = null!;

    public Symbol Id { get; }
    public ModalOptions Options { get; }
    public ModalHost Host { get; }
    public Modal? Modal { get; private set; }

    public Task WhenShown => _whenShownSource.Task;
    public Task WhenClosed => _whenClosedSource.Task;

    public ModalRef(ModalOptions options, ModalHost host)
    {
        Id = $"modal-{Ulid.NewUlid().ToString()}";
        Options = options;
        Host = host;
    }

    public bool Close(bool forceClose = false)
        => Host.Close(Id, forceClose);

    // IModalRefImpl implementation

    RenderFragment IModalRefImpl.View => _view;

    public void SetView(RenderFragment view)
        => _view = view;

    void IModalRefImpl.SetModal(Modal modal)
    {
        Modal = modal;
        _whenShownSource.TrySetResult(default);
    }

    void IModalRefImpl.MarkClosed()
        => _whenClosedSource.TrySetResult(default);
}
