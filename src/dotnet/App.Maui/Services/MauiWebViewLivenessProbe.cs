using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiWebViewLivenessProbe(IServiceProvider services)
{
    private readonly CancellationTokenSource _cancellationTokenSource = new ();

    private CancellationToken CancellationToken => _cancellationTokenSource.Token;
    private IServiceProvider Services { get; } = services;
    private ILogger Log { get; } = services.LogFor<MauiWebViewLivenessProbe>();

    public async Task StartCheck()
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
        // We give only 2000ms for one attempt to check aliveness, but not more than 300ms after
        // we ensures that main thread is free to pump up js invoke messages to WebView.
        // This optimization is need to reduce number of false WebView deadness detections.
        var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000));
        var cts2 = cancellationToken.LinkWith(cts1.Token);
        try {
            var scopedServices = await WhenScopedServicesReady(cts2.Token).ConfigureAwait(false);
            var jsRuntime = scopedServices.GetRequiredService<IJSRuntime>();
            _ = MainThread.InvokeOnMainThreadAsync(() => {
                cts2.CancelAfter(TimeSpan.FromMilliseconds(300));
            });
            var isAlive = await jsRuntime.Eval<bool>("true", cts2.Token).ConfigureAwait(false);
            return isAlive;
        }
        catch (Exception e) {
            var isSilent = e is OperationCanceledException or TimeoutException or JSDisconnectedException;
            if (!isSilent)
                Log.LogWarning(e, "An exception occurred during aliveness check");
        }
        return false;
    }

    private void OnDead()
    {
        Log.LogError("WebView is not alive. Will try to reload");
        Services.GetRequiredService<ReloadUI>().Reload();
    }
}
