using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;
using ActualLab.Diagnostics;

namespace ActualChat.App.Maui.Services;

public class MauiLivenessProbe : WorkerBase
{
    private static readonly TimeSpan VeryFirstCheckDelay = TimeSpan.FromSeconds(2); // JIT, etc., so might take longer
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan MainThreadBusyTimeout = TimeSpan.FromMilliseconds(45); // ~3 timer ticks
    private static readonly int CheckCount = 2; // 1s
    private static readonly int MainThreadBusyExtraCheckCount = 6; // 3s more

    private static readonly object _lock = new();
    private static MauiLivenessProbe? _current;
    private static volatile bool _isVeryFirstCheck = true;

    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(MauiLivenessProbe));
    private static ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public static MauiLivenessProbe? Current => _current;

    public static void Check(TimeSpan delay)
        => _ = Task.Delay(delay).ContinueWith(_ => Check(), TaskScheduler.Default);

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiLivenessProbe))]
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
        if (_isVeryFirstCheck) {
            _isVeryFirstCheck = false;
            await Task.Delay(VeryFirstCheckDelay, cancellationToken).ConfigureAwait(false);
        }
        var lastScopedServices = (IServiceProvider?)null;
        var lastCheckAt = CpuTimestamp.Now - TimeSpan.FromHours(1);
        var mustReload = false;
        var mainThreadBusyCheckCount = 0;
        while (!mustReload) {
            mustReload = true;
            for (int i = 0; i < CheckCount; i++) {
                var lastCheckDuration = lastCheckAt.Elapsed;
                await Task.Delay((CheckTimeout - lastCheckDuration).Positive(), cancellationToken).ConfigureAwait(false);

                var timeoutCts = new CancellationTokenSource();
                var timeoutToken = timeoutCts.Token;
                _ = Task.Delay(CheckTimeout, cancellationToken)
                    .ContinueWith(_ => timeoutCts.CancelAndDisposeSilently(), TaskScheduler.Default);

                lastCheckAt = CpuTimestamp.Now;
                var (scopedServices, error, isMainThreadBusy) = await Check(lastScopedServices, timeoutToken)
                    .ConfigureAwait(false);

                // No error
                if (error == null) {
                    DebugLog?.LogDebug("Liveness check #{Index}/{Count} succeeded", i, CheckCount);
                    return;
                }

                // Scoped services changed
                if (lastScopedServices != null && !ReferenceEquals(lastScopedServices, scopedServices)) {
                    DebugLog?.LogDebug(
                        "Liveness check #{Index}/{Count}: scoped services changed, restarting checks...",
                        i, CheckCount);
                    mustReload = false; // resets i to 0
                    break;
                }

                // Main thread is busy
                if (isMainThreadBusy) {
                    mainThreadBusyCheckCount++;
                    if (mainThreadBusyCheckCount <= MainThreadBusyExtraCheckCount) {
                        DebugLog?.LogDebug(error,
                            "Liveness check #{Index}/{Count}: the main thread is busy #{BusyCount}",
                            i, CheckCount, mainThreadBusyCheckCount);
                        i--;
                        continue;
                    }
                }

                // JS disconnected error
                if (error is JSDisconnectedException) {
                    DebugLog?.LogDebug(
                        "Liveness check #{Index}/{Count} failed (disconnected)",
                        i, CheckCount);
                    break;
                }
                DebugLog?.LogDebug(error,
                    "Liveness check #{Index}/{Count} failed (error)", i, CheckCount);
            }
        }

        Log.LogWarning("WebView is dead, reloading...");
        var appServices = await WhenAppServicesReady(cancellationToken).ConfigureAwait(false);
        appServices.GetRequiredService<ReloadUI>().Reload();
    }

    protected override Task OnStop()
    {
        CancelCheck(this);
        return Task.CompletedTask;
    }

    private static async Task<(IServiceProvider? ScopedServices, Exception? Error, bool IsMainThreadBusy)> Check(
        IServiceProvider? lastScopedServices,
        CancellationToken cancellationToken)
    {
        var scopedServices = (IServiceProvider?)null;
        var safeJSRuntime = (SafeJSRuntime?)null;
        try {
            if (TryGetScopedServices(out scopedServices)) {
                if (MauiWebView.Current?.IsDead == true)
                    return (scopedServices, JSRuntimeErrors.Disconnected(), false);
            }
            else
                scopedServices = await WhenScopedServicesReady(true, cancellationToken).ConfigureAwait(false);
            safeJSRuntime = scopedServices.GetRequiredService<SafeJSRuntime>();
            if (safeJSRuntime.IsDisconnected)
                return (scopedServices, JSRuntimeErrors.Disconnected(), false);

            var jsRuntime = scopedServices.GetRequiredService<IJSRuntime>();
            var browserInit = scopedServices.GetRequiredService<BrowserInit>();
            await browserInit.WhenInitialized.WaitAsync(cancellationToken).ConfigureAwait(false);
            var isAlive = await jsRuntime
                .InvokeAsync<bool>("window.ui.BrowserInit.isAlive", CancellationToken.None)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return (scopedServices, isAlive ? null : JSRuntimeErrors.Disconnected(), false);
        }
        catch (Exception e) {
            var whenScopedServicesChanged = WhenScopedServicesChanged(cancellationToken);
            var now = CpuTimestamp.Now;
            await MainThread.InvokeOnMainThreadAsync(() => { }).ConfigureAwait(false);
            var isMainThreadBusy = now.Elapsed >= MainThreadBusyTimeout;
            var isDisconnected = e is ObjectDisposedException || safeJSRuntime is { IsDisconnected: true };
            if (isDisconnected)
                return (scopedServices ?? lastScopedServices, JSRuntimeErrors.Disconnected(), isMainThreadBusy);
            try {
                // If there is some extra time, we can try pulling new scoped services
                scopedServices = await whenScopedServicesChanged.ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            return (scopedServices ?? lastScopedServices, e, isMainThreadBusy);
        }
    }
}
