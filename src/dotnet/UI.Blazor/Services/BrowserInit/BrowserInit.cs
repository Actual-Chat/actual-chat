using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInit(IServiceProvider services)
{
    private readonly TaskCompletionSource _whenInitializedSource = new();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async ValueTask Initialize(
        string apiVersion,
        string baseUri,
        string sessionHash,
        DotNetObjectReference<IBrowserInfoBackend> browserInfoBackendRef,
        AppKind appKind)
    {
        try {
            await services.JSRuntime()
                .InvokeVoidAsync("window.App.browserInit",
                    apiVersion,
                    baseUri,
                    sessionHash,
                    browserInfoBackendRef,
                    appKind.ToString())
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            services.LogFor<BrowserInit>().LogError(e, "An error occurred during browserInit call");
            throw;
        }
        finally {
            _whenInitializedSource.TrySetResult();
        }
    }
}
