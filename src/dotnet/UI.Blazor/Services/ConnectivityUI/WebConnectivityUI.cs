namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// WASM implementation: listens to navigator.onLine events via JS interop.
/// </summary>
public sealed class WebConnectivityUI : ConnectivityUI, IConnectivityUIBackend
{
    private readonly MutableState<bool> _isOnline;
    private readonly DotNetObjectReference<IConnectivityUIBackend> _backendRef;

    public override IState<bool> IsOnline => _isOnline;

    public WebConnectivityUI(UIHub hub) : base(hub)
    {
        _isOnline = hub.StateFactory.NewMutable(true, StateCategories.Get(GetType(), nameof(IsOnline)));
        _backendRef = DotNetObjectReference.Create<IConnectivityUIBackend>(this);
        hub.RegisterDisposable(_backendRef);
        _ = Initialize();
    }

    private async ValueTask Initialize()
    {
        try {
            await JS.InvokeVoidAsync(JSInitMethod, _backendRef).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
        }
    }

    [JSInvokable]
    public void OnOnlineChanged(bool isOnline)
        => _isOnline.Value = isOnline;
}
