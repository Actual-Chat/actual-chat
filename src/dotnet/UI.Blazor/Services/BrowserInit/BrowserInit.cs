using ActualChat.Rpc;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class BrowserInit(UIHub hub) : UIServiceBase<UIHub>(hub)
{
    private static readonly string JSInitFirebaseMethod = $"{BlazorUICoreModule.ImportName}.{nameof(BrowserInit)}.initFirebase";
    private static readonly string JSIsFirebaseConfiguredMethod = $"{BlazorUICoreModule.ImportName}.{nameof(BrowserInit)}.isFirebaseConfigured";

    private readonly AsyncTaskMethodBuilder _whenInitializedSource = AsyncTaskMethodBuilderExt.New();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public async Task Initialize(
        HostKind hostKind,
        AppKind appKind,
        string apiVersion,
        string baseUri,
        string sessionHash,
        AppConstants appConstants,
        DotNetObjectReference<IBrowserInfoBackend> browserInfoBlazorRef,
        DotNetObjectReference<IClipboardHandlers>? clipboardHandlersRef = null)
    {
        if (WhenInitialized.IsCompleted)
            return;

        try {
            await JS.InvokeVoidAsync("window.App.browserInit",
                    hostKind.ToString(),
                    appKind.ToString(),
                    apiVersion,
                    baseUri,
                    RpcEndpointSelector.Instance is null ? "" : RpcEndpointSelector.ApplyTo(baseUri),
                    HostInfo.GetHosts(),
                    sessionHash,
                    appConstants,
                    browserInfoBlazorRef,
                    clipboardHandlersRef)
                .ConfigureAwait(false);
            _whenInitializedSource.TrySetResult();
        }
        catch (Exception e) {
            Log.LogError(e, "An error occurred during browserInit call");
            _whenInitializedSource.TrySetException(e);
            throw;
        }
    }

    public async Task InitFirebase(bool isAnalyticsEnabled)
    {
        try {
            await JS.InvokeVoidAsync(JSInitFirebaseMethod, isAnalyticsEnabled).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "An error occurred during InitFirebase call");
            throw;
        }
    }

    public async Task<bool> IsFirebaseConfigured()
    {
        try {
            return await JS.InvokeAsync<bool>(JSIsFirebaseConfiguredMethod).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "An error occurred during InitFirebase call");
            throw;
        }
    }
}
