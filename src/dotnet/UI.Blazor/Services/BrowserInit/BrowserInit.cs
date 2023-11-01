using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInit(IJSRuntime js)
{
    private readonly TaskCompletionSource _whenInitializedSource = new();

    private IJSRuntime JS { get; } = js;

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async ValueTask Initialize(
        string apiVersion,
        string baseUri,
        string sessionHash,
        DotNetObjectReference<IBrowserInfoBackend> browserInfoBackendRef,
        AppKind appKind)
    {
        try {
            await JS
                .InvokeVoidAsync("window.App.browserInit", apiVersion, baseUri, sessionHash, browserInfoBackendRef, appKind.ToString())
                .ConfigureAwait(false);
        }
        finally {
            _whenInitializedSource.TrySetResult();
        }
    }
}
