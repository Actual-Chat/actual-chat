using ActualChat.UI.Blazor.Components.Internal;

namespace ActualChat.UI.Blazor.Components;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ModalRef : IHasId<Symbol>, IModalRefImpl
{
    private static long _lastId;
    private readonly TaskCompletionSource _whenShownSource = TaskCompletionSourceExt.New();
    private readonly TaskCompletionSource _whenClosedSource = TaskCompletionSourceExt.New();
    private RenderFragment _view = null!;
    private ModalStepRef? _modalStepRef = null;

    public Symbol Id { get; }
    public ModalOptions Options { get; }
    public ModalHost Host { get; }
    public object Model { get; }
    public Modal? Modal { get; private set; }
    public ModalStepRef? StepRef => _modalStepRef;

    public Task WhenShown => _whenShownSource.Task;
    public Task WhenClosed => _whenClosedSource.Task;

    public ModalRef(ModalOptions options, object model, ModalHost host)
    {
        Id = $"Modal-{model.GetType().GetName()}-{Interlocked.Increment(ref _lastId)}";
        Options = options;
        Model = model;
        Host = host;
    }

    public bool Close(bool force = false)
        => Host.Close(Id, force);

    // IModalRefImpl implementation

    RenderFragment IModalRefImpl.View => _view;

    public void SetView(RenderFragment view)
        => _view = view;

    public ModalStepRef StepIn(string name)
    {
        var stepRef = Host.HistoryStepper.StepIn(name);
        this._modalStepRef = new ModalStepRef(this, stepRef, this._modalStepRef);
        return this._modalStepRef;
    }

    void IModalRefImpl.SetModal(Modal modal)
    {
        Modal = modal;
        _whenShownSource.TrySetResult();
    }

    void IModalRefImpl.MarkClosed()
        => _whenClosedSource.TrySetResult();

    void IModalRefImpl.CloseSteps()
    {
        while (_modalStepRef != null) {
            _modalStepRef.Close(true);
            this._modalStepRef = _modalStepRef.ParentStepRef;
        }
    }

    public bool StepBack()
    {
        if (_modalStepRef == null)
            return false;
        _modalStepRef.Close(false);
        this._modalStepRef = _modalStepRef.ParentStepRef;
        return true;
    }
}
