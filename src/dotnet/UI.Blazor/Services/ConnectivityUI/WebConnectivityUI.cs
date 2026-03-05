namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// WASM implementation: listens to navigator.onLine events via JS interop.
/// </summary>
public sealed class WebConnectivityUI : ConnectivityUI, IConnectivityUIBackend
{
    private readonly MutableState<bool> _isOnline;

    public override IState<bool> IsOnline => _isOnline;

    public WebConnectivityUI(UIHub hub) : base(hub)
    {
        _isOnline = hub.StateFactory.NewMutable(true, StateCategories.Get(GetType(), nameof(IsOnline)));
        var backendRef = DotNetObjectReference.Create<IConnectivityUIBackend>(this);
        hub.RegisterDisposable(backendRef);
        _ = Initialize(backendRef);
    }

    [JSInvokable]
    public void OnOnlineChanged(bool isOnline)
        => _isOnline.Value = isOnline;
}
