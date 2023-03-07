using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ModalRef : IHasId<Symbol>, IModalRefImpl
{
    private static long _lastId;
    private readonly TaskSource<Unit> _whenShownSource = TaskSource.New<Unit>(true);
    private readonly TaskSource<Unit> _whenClosedSource = TaskSource.New<Unit>(true);
    private RenderFragment _view = null!;

    public Symbol Id { get; }
    public ModalOptions Options { get; }
    public ModalHost Host { get; }
    public object Model { get; }
    public Modal? Modal { get; private set; }

    public Task WhenShown => _whenShownSource.Task;
    public Task WhenClosed => _whenClosedSource.Task;

    public ModalRef(ModalOptions options, object model, ModalHost host)
    {
        Id = $"Modal-{model.GetType().GetName()}-{Interlocked.Increment(ref _lastId)}";
        Options = options;
        Model = model;
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
