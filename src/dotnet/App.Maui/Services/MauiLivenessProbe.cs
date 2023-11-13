using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiLivenessProbe : WorkerBase
{
    private static readonly TimeSpan VeryFirstCheckDelay = TimeSpan.FromSeconds(2.5); // JIT, etc., so might take longer
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(0.5); // WebView reload?
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan DisconnectToRetryDelay = TimeSpan.FromSeconds(0.05);
    private static readonly int CheckCount = 4; // 2s

    private static readonly object _lock = new();
    private static MauiLivenessProbe? _instance;
    private static volatile bool _isVeryFirstCheck = true;
    private static ILogger? _log;

    private static ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger<MauiLivenessProbe>();

    public static void Check()
    {
        lock (_lock)
            _instance ??= new MauiLivenessProbe();
    }

    public static void CancelCheck(MauiLivenessProbe? instance = null)
    {
        lock (_lock) {
            if (instance != null && instance != _instance)
                return;

            _instance.DisposeSilently();
            _instance = null;
        }
    }

    protected MauiLivenessProbe(bool mustStart = true)
    {
        if (mustStart)
            this.Start();
    }

    // Protected & private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var lastScopedServices = (IServiceProvider?)null;
        var lastCheckAt = CpuTimestamp.Now;
        var mustReload = false;
        while (!mustReload) {
            mustReload = true;
            var disconnectedCount = 0;
            for (int i = 0; i < CheckCount; i++) {
                var lastCheckDuration = lastCheckAt.Elapsed;
                var delay = i == 0
                    ? _isVeryFirstCheck ? VeryFirstCheckDelay : FirstCheckDelay
                    : CheckTimeout;
                await Task.Delay((delay - lastCheckDuration).Positive(), cancellationToken).ConfigureAwait(false);

                var timeoutCts = new CancellationTokenSource();
                var timeoutToken = timeoutCts.Token;
                _ = Task.Delay(CheckTimeout, cancellationToken)
                    .ContinueWith(_ => timeoutCts.CancelAndDisposeSilently(), TaskScheduler.Default);

                lastCheckAt = CpuTimestamp.Now;
                var (scopedServices, error) = await Check(lastScopedServices, timeoutToken).ConfigureAwait(false);
                if (error == null) {
                    _isVeryFirstCheck = false;
                    Log.LogInformation("Liveness check #{Index}/{Count} succeeded", i, CheckCount);
                    return;
                }
                if (lastScopedServices != null && !ReferenceEquals(lastScopedServices, scopedServices)) {
                    Log.LogInformation(
                        "Liveness check #{Index}/{Count}: scoped services changed, restarting checks...",
                        i, CheckCount);
                    mustReload = false;
                    break;
                }
                if (error is JSDisconnectedException) {
                    disconnectedCount++;
                    Log.LogWarning("Liveness check #{Index}/{Count} failed (disconnected {DisconnectedCount} time(s))",
                        i, CheckCount, disconnectedCount);
                    if (disconnectedCount >= 3)
                        break;
                }
                else
                    Log.LogWarning(error, "Liveness check #{Index}/{Count} failed", i, CheckCount);
            }
        }

        Log.LogError("WebView is dead, reloading...");
        var appServices = await WhenAppServicesReady(CancellationToken.None).ConfigureAwait(false);
        appServices.GetRequiredService<ReloadUI>().Reload();
    }

    protected override Task OnStop()
    {
        CancelCheck(this);
        return Task.CompletedTask;
    }

    private async Task<(IServiceProvider?, Exception?)> Check(
        IServiceProvider? lastScopedServices,
        CancellationToken cancellationToken)
    {
        var scopedServices = (IServiceProvider?)null;
        var safeJSRuntime = (SafeJSRuntime?)null;
        try {
            scopedServices = await WhenScopedServicesReady(true, cancellationToken).ConfigureAwait(false);
            safeJSRuntime = scopedServices.GetRequiredService<SafeJSRuntime>();
            if (safeJSRuntime.IsDisconnected)
                return (scopedServices, JSRuntimeErrors.Disconnected());

            var jsRuntime = scopedServices.GetRequiredService<IJSRuntime>();
            var browserInit = scopedServices.GetRequiredService<BrowserInit>();
            await browserInit.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            var isAlive = await jsRuntime
                .InvokeAsync<bool>("window.ui.BrowserInit.isAlive", cancellationToken)
                .ConfigureAwait(false);
            return (scopedServices, isAlive ? null : JSRuntimeErrors.Disconnected());
        }
        catch (Exception e) {
            var isDisconnected = safeJSRuntime is { IsDisconnected: true };
            if (!isDisconnected)
                return (scopedServices ?? lastScopedServices, e);

            try {
                // Let's try to pull new scoped services on disconnect
                await Task.Delay(DisconnectToRetryDelay, cancellationToken).ConfigureAwait(false);
                scopedServices = await WhenScopedServicesReady(true, cancellationToken).ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            return (scopedServices ?? lastScopedServices, JSRuntimeErrors.Disconnected());
        }
    }
}
