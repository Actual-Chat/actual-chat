namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Cascaded by <see cref="ContentSwapLayer"/>. A component that registers itself into an area it
/// doesn't own must hold that registration only while <see cref="IsVisible"/> is set - a layer
/// that isn't displayed is either still building underneath the one on screen, or has already
/// handed over to the layer that replaced it.
/// </summary>
public sealed class ContentSwapContext
{
    private readonly ContentSwapContext? _parent;
    private ContentSwapLayerState _ownState;

    public ContentSwapLayerState State { get; private set; }
    public bool IsVisible => State.IsVisible();
    public bool CanRender => State.CanRender();
    public event Action? StateChanged;

    public ContentSwapContext(ContentSwapContext? parent = null)
    {
        // The parent is the context of the enclosing ContentSwap's layer, if there is one. Consumers
        // bind to the nearest context only, so without this chain a nested ContentSwap would shadow
        // the outer signal - and an inner layer is on screen only while the outer one it sits in is.
        _parent = parent;
        if (parent != null)
            parent.StateChanged += Update;
        Update();
    }

    // Protected/internal methods

    internal void Detach()
    {
        // Called when ContentSwap drops the layer: until then the parent's event holds this context
        // alive, and a layer that comes and goes while the parent stays would pile up there.
        if (_parent != null)
            _parent.StateChanged -= Update;
    }

    internal void SetState(ContentSwapLayerState state)
    {
        // Ignoring a step backwards rather than asserting: the states are a one-way progression, and
        // the paired Displayed/Replaced calls of a superseded swap would otherwise have to be ordered
        // by their callers instead of by the type.
        if (state <= _ownState)
            return;

        _ownState = state;
        Update();
    }

    // Private methods

    private void Update()
    {
        // An outer layer that is on its way out takes everything inside it along, displayed or not
        var state = _parent is null || _parent.State == ContentSwapLayerState.Displayed
            ? _ownState
            : _parent.State;
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke();
    }
}
