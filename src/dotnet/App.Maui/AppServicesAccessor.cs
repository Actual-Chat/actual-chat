using ActualChat.App.Maui.Services;
using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;
using Stl.Internal;

namespace ActualChat.App.Maui;

public class AppServicesAccessor
{
    private static readonly TimeSpan WhenRenderedTimeout = TimeSpan.FromSeconds(3);

    private static readonly object _lock = new();
    private static ILogger? _log;
    private static volatile IServiceProvider? _appServices;
    private static volatile IServiceProvider? _scopedServices;
    private static readonly TaskCompletionSource<IServiceProvider> _appServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _scopedServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();

    private static ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger<AppServicesAccessor>();

    public static IServiceProvider AppServices {
        get => _appServices ?? throw Errors.NotInitialized(nameof(AppServices));
        set {
            lock (_lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_appServices, value))
                    return;
                if (_appServices != null)
                    throw Errors.AlreadyInitialized(nameof(AppServices));

                _appServices = value;
                _appServicesSource.TrySetResult(value);
                Log.LogDebug("AppServices ready");
            }
        }
    }

    public static IServiceProvider ScopedServices {
        get => _scopedServices ?? throw Errors.NotInitialized(nameof(ScopedServices));
        set {
            lock (_lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_scopedServices, value))
                    return;
                if (_scopedServices != null)
                    throw Errors.AlreadyInitialized(nameof(ScopedServices));

                _scopedServices = value;
                _scopedServicesSource.TrySetResult(value);
                Log.LogDebug("ScopedServices ready");
            }
        }
    }

    public static Task<IServiceProvider> WhenAppServicesReady(CancellationToken cancellationToken = default)
        => _appServicesSource.Task.WaitAsync(cancellationToken);

    public static bool TryGetScopedServices([NotNullWhen(true)] out IServiceProvider? scopedServices)
    {
        scopedServices = _scopedServices;
        return scopedServices != null;
    }

    public static Task<IServiceProvider> WhenScopedServicesReady(CancellationToken cancellationToken = default)
        => WhenScopedServicesReady(false, cancellationToken);
    public static Task<IServiceProvider> WhenScopedServicesReady(bool whenRendered, CancellationToken cancellationToken = default)
    {
        var scopedServicesTask = _scopedServicesSource.Task;
        if (scopedServicesTask.IsCompletedSuccessfully) {
            var c = scopedServicesTask.Result;
            var blazorCircuitContext = c.GetRequiredService<AppBlazorCircuitContext>();
            if (blazorCircuitContext.WhenReady.IsCompletedSuccessfully) {
                if (!whenRendered)
                    return scopedServicesTask;

                var loadingUI = c.GetRequiredService<LoadingUI>();
                if (loadingUI.WhenRendered.IsCompletedSuccessfully)
                    return scopedServicesTask;
            }
        }

        return WhenScopedServicesReadyAsync(whenRendered, cancellationToken);

        static async Task<IServiceProvider> WhenScopedServicesReadyAsync(bool whenRendered, CancellationToken cancellationToken) {
            var mustWait = false;
            while (true) {
                if (mustWait) // We don't want to rapidly cycle here
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                mustWait = true;

                var scopedServicesTask = _scopedServicesSource.Task;
                await scopedServicesTask.SilentAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!scopedServicesTask.IsCompletedSuccessfully || scopedServicesTask.Result is not { } c)
                    continue;

                if (whenRendered) {
                    var loadingUI = c.GetRequiredService<LoadingUI>();
                    await loadingUI.WhenRendered
                        .WaitAsync(WhenRenderedTimeout, cancellationToken)
                        .SilentAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (loadingUI.WhenRendered.IsCompletedSuccessfully)
                        return c;
                }
                else {
                    var blazorCircuitContext = c.GetRequiredService<AppBlazorCircuitContext>();
                    await blazorCircuitContext.WhenReady
                        .WaitAsync(WhenRenderedTimeout, cancellationToken)
                        .SilentAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (blazorCircuitContext.WhenReady.IsCompletedSuccessfully)
                        return c;
                }
            }
        }
    }

    public static Task DispatchToBlazor(Action<IServiceProvider> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => _ = ForegroundTask.Run(() => {
            workItem.Invoke(c);
            return Task.CompletedTask;
        }, Log, $"{name} failed"));

    public static Task DispatchToBlazor(Func<IServiceProvider, Task> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => ForegroundTask.Run(
            async () => await workItem.Invoke(c).ConfigureAwait(false),
            Log, $"{name} failed"));

    public static Task DispatchToBlazor<T>(Func<IServiceProvider, Task<T>> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => ForegroundTask.Run(
            async () => await workItem.Invoke(c).ConfigureAwait(false),
            Log, $"{name} failed"));

    public static async Task DispatchToBlazor(Action<IServiceProvider> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenScopedServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        await dispatcher.InvokeAsync(() => workItem.Invoke(scopedServices)).ConfigureAwait(false);
    }

    public static async Task DispatchToBlazor(Func<IServiceProvider, Task> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenScopedServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        await dispatcher.InvokeAsync(() => workItem.Invoke(scopedServices)).ConfigureAwait(false);
    }

    public static async Task<T> DispatchToBlazor<T>(Func<IServiceProvider, Task<T>> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenScopedServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        return await dispatcher.InvokeAsync(() => workItem.Invoke(scopedServices)).ConfigureAwait(false);
    }

    public static void DiscardScopedServices(IServiceProvider? expectedScopedServices = null)
    {
        lock (_lock) {
            var scopedServices = _scopedServices;
            if (scopedServices == null)
                return;
            if (expectedScopedServices != null && !ReferenceEquals(scopedServices, expectedScopedServices)) {
                DisconnectSafeJSRuntime(expectedScopedServices);
                return;
            }

            _scopedServicesSource.TrySetCanceled();
            _scopedServicesSource = TaskCompletionSourceExt.New<IServiceProvider>(); // Must go first
            _scopedServices = null;
            DisconnectSafeJSRuntime(scopedServices);
        }
        AppServices.LogFor(nameof(AppServicesAccessor)).LogDebug("ScopedServices discarded");

        static void DisconnectSafeJSRuntime(IServiceProvider services)
        {
            try {
                if (services.GetService<SafeJSRuntime>() is { } safeJSRuntime)
                    safeJSRuntime.MarkDisconnected();
            }
            catch {
                // Intended: services might be already disposed, so GetService may fail
            }
        }
    }
}
