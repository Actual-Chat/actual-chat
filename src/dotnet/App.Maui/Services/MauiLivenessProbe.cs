using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services;

public class MauiLivenessProbe : WorkerBase
{
    private static readonly TimeSpan VeryFirstCheckDelay = TimeSpan.FromSeconds(3); // JIT, etc., so might take longer
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(2); // WebView reload?
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan MainThreadBusyTimeout = TimeSpan.FromMilliseconds(45); // ~3 timer ticks
    private static readonly int CheckCount = 6; // 3s
    private static readonly int MainThreadBusyExtraCheckCount = 6; // 3s more
    private static readonly int DisconnectedCheckCount = 3; // 1.5s

    private static readonly object _lock = new();
    private static MauiLivenessProbe? _current;
    private static volatile bool _isVeryFirstCheck = true;
    private static ILogger? _log;

    private static ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger<MauiLivenessProbe>();

    public static MauiLivenessProbe? Current => _current;

    public static void Check()
    {
        lock (_lock)
            _current ??= new MauiLivenessProbe();
    }

    public static void CancelCheck(MauiLivenessProbe? expectedCurrent = null)
    {
        lock (_lock) {
            if (expectedCurrent != null && expectedCurrent != _current)
                return;

            _current.DisposeSilently();
            _current = null;
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
        var mainThreadBusyCheckCount = 0;
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
                var (scopedServices, error, isMainThreadBusy) = await Check(lastScopedServices, timeoutToken)
                    .ConfigureAwait(false);

                // No error
                if (error == null) {
                    _isVeryFirstCheck = false;
                    Log.LogInformation("Liveness check #{Index}/{Count} succeeded", i, CheckCount);
                    return;
                }

                // Scoped services changed
                if (lastScopedServices != null && !ReferenceEquals(lastScopedServices, scopedServices)) {
                    Log.LogInformation(
                        "Liveness check #{Index}/{Count}: scoped services changed, restarting checks...",
                        i, CheckCount);
                    mustReload = false; // resets i to 0
                    break;
                }

                // Main thread is busy
                if (isMainThreadBusy) {
                    mainThreadBusyCheckCount++;
                    if (mainThreadBusyCheckCount <= MainThreadBusyExtraCheckCount) {
                        Log.LogWarning(error,
                            "Liveness check #{Index}/{Count}: the main thread is busy #{BusyCount}",
                            i, CheckCount, mainThreadBusyCheckCount);
                        i--;
                        continue;
                    }
                }

                // JS disconnected error
                if (error is JSDisconnectedException) {
                    disconnectedCount++;
                    Log.LogWarning(
                        "Liveness check #{Index}/{Count} failed (disconnected {DisconnectedCount} time(s))",
                        i, CheckCount, disconnectedCount);
                    if (disconnectedCount >= DisconnectedCheckCount)
                        break;
                }
                else
                    Log.LogWarning(error,
                        "Liveness check #{Index}/{Count} failed (error)", i, CheckCount);
            }
        }

        Log.LogError("WebView is dead, reloading...");
        var appServices = await WhenAppServicesReady(cancellationToken).ConfigureAwait(false);
        appServices.GetRequiredService<ReloadUI>().Reload();
    }

    protected override Task OnStop()
    {
        CancelCheck(this);
        return Task.CompletedTask;
    }

    private async Task<(IServiceProvider? ScopedServices, Exception? Error, bool IsMainThreadBusy)> Check(
        IServiceProvider? lastScopedServices,
        CancellationToken cancellationToken)
    {
        var scopedServices = (IServiceProvider?)null;
        var safeJSRuntime = (SafeJSRuntime?)null;
        try {
            scopedServices = await WhenScopedServicesReady(true, cancellationToken).ConfigureAwait(false);
            safeJSRuntime = scopedServices.GetRequiredService<SafeJSRuntime>();
            if (safeJSRuntime.IsDisconnected)
                return (scopedServices, JSRuntimeErrors.Disconnected(), false);

            var jsRuntime = scopedServices.GetRequiredService<IJSRuntime>();
            var browserInit = scopedServices.GetRequiredService<BrowserInit>();
            await browserInit.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            var isAlive = await jsRuntime
                .InvokeAsync<bool>("window.ui.BrowserInit.isAlive", cancellationToken)
                .ConfigureAwait(false);
            return (scopedServices, isAlive ? null : JSRuntimeErrors.Disconnected(), false);
        }
        catch (Exception e) {
            var whenScopedServicesChanged = WhenScopedServicesChanged(cancellationToken);
            var now = CpuTimestamp.Now;
            await MainThread.InvokeOnMainThreadAsync(() => { }).ConfigureAwait(false);
            var isMainThreadBusy = now.Elapsed >= MainThreadBusyTimeout;
            var error = safeJSRuntime is { IsDisconnected: true } ? JSRuntimeErrors.Disconnected() : e;
            try {
                // If there is some extra time, we can try pulling new scoped services
                scopedServices = await whenScopedServicesChanged.ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            return (scopedServices ?? lastScopedServices, error, isMainThreadBusy);
        }
    }
}
