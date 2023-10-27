using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiWebViewLivenessProbe(IServiceProvider services)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new ();
    private IDispatcher? _dispatcher;

    private CancellationToken CancellationToken => _cancellationTokenSource.Token;
    private IServiceProvider Services { get; } = services;
    private ILogger Log { get; } = services.LogFor<MauiWebViewLivenessProbe>();
    private IDispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<IDispatcher>();

    public Task StartCheck()
        => BackgroundTask.Run(StartCheckInternal, CancellationToken);

    public async Task StartCheckInternal()
    {
        Log.LogDebug("Starting check");
        try {
            for (int i = 0; i < 4; i++) {
                if (i > 0)
                    await Task.Delay(300, CancellationToken).ConfigureAwait(false);
                var isAlive = await IsAlive(CancellationToken).ConfigureAwait(false);
                if (isAlive) {
                    Log.LogDebug("Check passed");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        if (CancellationToken.IsCancellationRequested) {
            Log.LogDebug("Check cancelled");
            return;
        }
        OnDead();
    }

    public void StopCheck()
        => _cancellationTokenSource.Cancel();

    private async Task<bool> IsAlive(CancellationToken cancellationToken)
    {
        // We give only 2000ms for one attempt to check aliveness, but not more than 500ms after
        // we have ensured that main thread is free after dispatching js invoke message to WebView.
        // This optimization is need to reduce number of false WebView deadness detections.
        var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
        var cts2 = cancellationToken.LinkWith(cts1.Token);
        try {
            var scopedServices = await WhenScopedServicesReady(cts2.Token).ConfigureAwait(false);
            var jsRuntime = scopedServices.GetRequiredService<IJSRuntime>();
            var browserInit = scopedServices.GetRequiredService<BrowserInit>();
            var checkState = browserInit.WhenInitialized.IsCompletedSuccessfully;
            var script = checkState ? "window.ui.BrowserInit.isStateOk" : "window.App.isBundleReady";
            var isAliveTask = jsRuntime.InvokeAsync<bool>(script, cts2.Token);
            _ = Dispatcher.DispatchAsync(() => {
                // Will cancel check in 500ms after BeginInvokeJS message is dispatched.
                try {
                    cts2.CancelAfter(TimeSpan.FromMilliseconds(500));
                }
                catch(ObjectDisposedException){}
            });
            var isAlive = await isAliveTask.ConfigureAwait(false);
            return isAlive;
        }
        catch (Exception e) {
            var isSilent = e is OperationCanceledException or TimeoutException or JSDisconnectedException;
            if (!isSilent)
                Log.LogWarning(e, "An exception occurred during aliveness check");
        }
        finally {
            cts2.DisposeSilently();
            cts1.DisposeSilently();
        }
        return false;
    }

    private void OnDead()
    {
        Log.LogError("WebView is not alive. Will try to reload");
        Services.GetRequiredService<ReloadUI>().Reload();
    }
}
