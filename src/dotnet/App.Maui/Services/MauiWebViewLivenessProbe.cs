using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiWebViewLivenessProbe
{
    private readonly CancellationTokenSource _cancellationTokenSource = new ();

    private CancellationToken CancellationToken => _cancellationTokenSource.Token;
    private IServiceProvider Services { get; }

    public MauiWebViewLivenessProbe(IServiceProvider services)
        => Services = services;

    public async Task StartCheck()
    {
        for (int i = 0; i < 4; i++) {
            if (i > 0)
                await Task.Delay(300, CancellationToken).ConfigureAwait(false);
            var isAlive = await IsAlive(CancellationToken).ConfigureAwait(false);
            if (isAlive)
                return;
        }
        if (CancellationToken.IsCancellationRequested)
            return;
        OnDead();
    }

    public void StopCheck()
        => _cancellationTokenSource.Cancel();

    private async Task<bool> IsAlive(CancellationToken cancellationToken)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));
        try {
            var services = await ScopedServicesTask.WaitAsync(cts.Token).ConfigureAwait(false);
            var jsRuntime = services.GetRequiredService<IJSRuntime>();
            var isAlive = await jsRuntime.Eval<bool>("true", cts.Token).ConfigureAwait(false);
            return isAlive;
        }
        catch (Exception e) {
            var silent =
                e is OperationCanceledException ||
                e is TimeoutException ||
                e is JSDisconnectedException;
            if (!silent) {
                Services.LogFor<MauiWebViewLivenessProbe>()
                    .LogWarning(e, "An exception occurred during maui web view aliveness check");
            }
        }
        return false;
    }

    private void OnDead()
    {
        Services!.LogFor<MauiWebViewLivenessProbe>()
            .LogError("WebView is not alive. Will try to reload");
        Services!.GetRequiredService<ReloadUI>().Reload();
    }
}
