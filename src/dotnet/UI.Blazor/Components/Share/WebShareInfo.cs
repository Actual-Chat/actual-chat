using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public class WebShareInfo : IDisposable, IWebShareInfoBackend
{
    private bool _canShareText;
    private bool _canShareLink;

    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();
    private DotNetObjectReference<IWebShareInfoBackend>? _backendRef;

    private Task WhenReady => _whenReadySource.Task;
    private IJSRuntime JS { get; }
    private ILogger Log { get; }

    public WebShareInfo(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        JS = services.JSRuntime();
        _ = Initialize();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public async Task<bool> CanShareText()
    {
        await WhenReady.ConfigureAwait(true);
        return _canShareText;
    }

    public async Task<bool> CanShareLink()
    {
        await WhenReady.ConfigureAwait(true);
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

    private ValueTask Initialize()
    {
        var jsMethod = $"{BlazorUICoreModule.ImportName}.Share.initWebShareInfo";
        _backendRef = DotNetObjectReference.Create<IWebShareInfoBackend>(this);
        return JS.InvokeVoidAsync(jsMethod, _backendRef);
    }
}
