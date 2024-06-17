using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class BrowserInit(IServiceProvider services)
{
    private static readonly string JSInitFirebaseMethod = $"{BlazorUICoreModule.ImportName}.{nameof(BrowserInit)}.initFirebase";

    private readonly TaskCompletionSource _whenInitializedSource = new();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async Task Initialize(
        HostKind hostKind,
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
}
