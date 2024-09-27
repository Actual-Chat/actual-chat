using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInit(IServiceProvider services)
{
    private static readonly string JSInitFirebaseMethod = $"{BlazorUICoreModule.ImportName}.{nameof(BrowserInit)}.initFirebase";
    private static readonly string JSIsFirebaseConfiguredMethod = $"{BlazorUICoreModule.ImportName}.{nameof(BrowserInit)}.isFirebaseConfigured";

    private readonly TaskCompletionSource _whenInitializedSource = new();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async Task Initialize(
        HostKind hostKind,
        AppKind appKind,
        string apiVersion,
        string baseUri,
        string sessionHash,
        DotNetObjectReference<IBrowserInfoBackend> browserInfoBlazorRef)
    {
        if (WhenInitialized.IsCompleted)
            return;

        try {
            var js = services.JSRuntime();
            await js
                .InvokeVoidAsync("window.App.browserInit",
                    hostKind.ToString(),
                    appKind.ToString(),
                    apiVersion,
                    baseUri,
                    sessionHash,
                    browserInfoBlazorRef)
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

    public async Task InitFirebase(bool isAnalyticsEnabled)
    {
        try {
            var js = services.JSRuntime();
            await js
                .InvokeVoidAsync(JSInitFirebaseMethod,
                    isAnalyticsEnabled)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            var log = services.LogFor(GetType());
            log.LogError(e, "An error occurred during InitFirebase call");
            throw;
        }
    }

    public async Task<bool> IsFirebaseConfigured()
    {
        try {
            var js = services.JSRuntime();
            return await js
                .InvokeAsync<bool>(JSIsFirebaseConfiguredMethod)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            var log = services.LogFor(GetType());
            log.LogError(e, "An error occurred during InitFirebase call");
            throw;
        }
    }
}
