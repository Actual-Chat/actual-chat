using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public sealed class WebShareInfo : IDisposable, IWebShareInfoBackend
{
    private static readonly string JSInitWebShareInfoMethod = $"{BlazorUICoreModule.ImportName}.Share.init";

    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();
    private readonly DotNetObjectReference<IWebShareInfoBackend>? _backendRef;
    private bool _canShareText;
    private bool _canShareLink;

    private Task WhenReady => _whenReadySource.Task;
    private IJSRuntime JS { get; }
    private ILogger Log { get; }

    public WebShareInfo(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        JS = services.JSRuntime();
        _backendRef = DotNetObjectReference.Create<IWebShareInfoBackend>(this);
        _ = JS.InvokeVoidAsync(JSInitWebShareInfoMethod, _backendRef);
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public async ValueTask<bool> CanShare()
    {
        await WhenReady.ConfigureAwait(true);
        return _canShareText || _canShareLink;
    }

    public async ValueTask<bool> CanShareText()
    {
        await WhenReady.ConfigureAwait(true);
        return _canShareText;
    }

    public async ValueTask<bool> CanShareLink()
    {
        await WhenReady.ConfigureAwait(true);
        return _canShareLink;
    }

    [JSInvokable]
    public void OnInitialized(object sender, IWebShareInfoBackend.InitResult initResult)
    {
        Log.LogDebug("OnInitialized: {InitResult}", initResult);
        _canShareText = initResult.CanShareText;
        _canShareLink = initResult.CanShareLink;
        _whenReadySource.TrySetResult();
    }
}
