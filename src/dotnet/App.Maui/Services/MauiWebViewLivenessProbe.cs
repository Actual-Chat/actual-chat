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
        // We give only 2000ms for one attempt to check liveness, but not more than 500ms after
        // we have ensured that the main thread is free after dispatching JS invoke message to WebView.
        // This optimization is needed to reduce the number of false negatives here.
        var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
        var commonCts = cancellationToken.LinkWith(timeoutCts.Token);
        var stopToken = commonCts.Token;
        try {
            var scopedServices = await WhenScopedServicesReady(stopToken).ConfigureAwait(false);
            var js = scopedServices.GetRequiredService<IJSRuntime>();
            var browserInit = scopedServices.GetRequiredService<BrowserInit>();
            var checkState = browserInit.WhenInitialized.IsCompletedSuccessfully;
            var jsInvokeTarget = checkState ? "window.ui.BrowserInit.isAlive" : "window.App.isBundleReady";
            var isAliveTask = js.InvokeAsync<bool>(jsInvokeTarget, stopToken);
            _ = Dispatcher.DispatchAsync(async () => {
                // Will cancel the check in 500ms after BeginInvokeJS message is dispatched.
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                commonCts.CancelAndDisposeSilently();
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
            commonCts.CancelAndDisposeSilently();
            timeoutCts.CancelAndDisposeSilently();
        }
        return false;
    }

    private void OnDead()
    {
        Log.LogError("WebView is not alive. Will try to reload");
        Services.GetRequiredService<ReloadUI>().Reload();
    }
}
