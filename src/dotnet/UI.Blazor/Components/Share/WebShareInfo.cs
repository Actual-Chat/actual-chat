using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Components;

public sealed class WebShareInfo : UIServiceBase<UIHub>, IDisposable, IWebShareInfoBackend
{
    private static readonly string JSInitWebShareInfoMethod = $"{BlazorUICoreModule.ImportName}.Share.init";

    private readonly AsyncTaskMethodBuilder _whenReadySource = AsyncTaskMethodBuilderExt.New();
    private readonly DotNetObjectReference<IWebShareInfoBackend>? _backendRef;
    private bool _canShareText;
    private bool _canShareLink;

    private Task WhenReady => _whenReadySource.Task;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WebShareInfo))]
    public WebShareInfo(UIHub hub) : base(hub)
    {
        _backendRef = DotNetObjectReference.Create<IWebShareInfoBackend>(this);
        _ = JS.InvokeVoidAsync(JSInitWebShareInfoMethod, _backendRef);
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public async ValueTask<bool> CanShare()
    {
        await WhenReady;
        return _canShareText || _canShareLink;
    }

    public async ValueTask<bool> CanShareText()
    {
        await WhenReady;
        return _canShareText;
    }

    public async ValueTask<bool> CanShareLink()
    {
        await WhenReady;
        return _canShareLink;
    }

    [JSInvokable]
    public void OnInitialized(IWebShareInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);
        _canShareText = initResult.CanShareText;
        _canShareLink = initResult.CanShareLink;
        _whenReadySource.TrySetResult();
    }
}
