using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInit(IServiceProvider services)
{
    private readonly TaskCompletionSource _whenInitializedSource = new();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async Task Initialize(
        AppKind appKind,
        string apiVersion,
        string baseUri,
        string sessionHash,
        DotNetObjectReference<IBrowserInfoBackend> browserInfoBackendRef)
    {
        if (WhenInitialized.IsCompleted)
            return;

        try {
            var js = services.JSRuntime();
            await js
                .InvokeVoidAsync("window.App.browserInit",
                    appKind.ToString(),
                    apiVersion,
                    baseUri,
                    sessionHash,
                    browserInfoBackendRef)
                .ConfigureAwait(false);
            _whenInitializedSource.TrySetResult();
        }
        catch (Exception e) {
            var log = services.LogFor(GetType());
            log.LogError(e, "An error occurred during browserInit call");
            _whenInitializedSource.TrySetException(e);
            throw;
        }
    }
}
