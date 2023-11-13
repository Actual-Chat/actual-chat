using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Stl.Internal;

namespace ActualChat.App.Maui;

public class AppServicesAccessor
{
    private static readonly TimeSpan WhenRenderedTimeout = TimeSpan.FromSeconds(3);

    private static readonly object StaticLock = new();
    private static ILogger? _appServicesAccessorLog; // Otherwise Rider assumes we're referencing it from elsewhere
    private static volatile IServiceProvider? _appServices;
    private static volatile IServiceProvider? _scopedServices;
    private static readonly TaskCompletionSource<IServiceProvider> _appServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _scopedServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _scopedServicesChangedSource =
        TaskCompletionSourceExt.New<IServiceProvider>();

    private static ILogger Log => _appServicesAccessorLog ??= MauiDiagnostics.LoggerFactory.CreateLogger<AppServicesAccessor>();

    public static IServiceProvider AppServices {
        get => _appServices ?? throw Errors.NotInitialized(nameof(AppServices));
        set {
            lock (StaticLock) {
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
            lock (StaticLock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_scopedServices, value))
                    return;
                if (_scopedServices != null)
                    TryDiscardActiveScopedServices(_scopedServices, "ScopedServices.set");

                _scopedServices = value;
                _scopedServicesSource.TrySetResult(value);
                _scopedServicesChangedSource.TrySetResult(value);
                _scopedServicesChangedSource = TaskCompletionSourceExt.New<IServiceProvider>();
                Log.LogDebug("ScopedServices ready");
            }
        }
    }

    public static bool TryGetScopedServices([NotNullWhen(true)] out IServiceProvider? scopedServices)
    {
        scopedServices = _scopedServices;
        return scopedServices != null;
    }

    public static Task<IServiceProvider> WhenAppServicesReady(CancellationToken cancellationToken = default)
        => _appServicesSource.Task.WaitAsync(cancellationToken);

    public static Task<IServiceProvider> WhenScopedServicesChanged(CancellationToken cancellationToken = default)
        => _scopedServicesChangedSource.Task.WaitAsync(cancellationToken);
    public static Task<IServiceProvider> WhenScopedServicesReady(CancellationToken cancellationToken = default)
        => WhenScopedServicesReady(false, cancellationToken);
    public static Task<IServiceProvider> WhenScopedServicesReady(
        bool whenRendered, CancellationToken cancellationToken = default)
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

    // Private methods

    private static async Task<IServiceProvider> WhenScopedServicesReadyAsync(
        bool whenRendered, CancellationToken cancellationToken)
    {
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

    public static void TryDiscardActiveScopedServices(IServiceProvider? scopedServices, string reason)
    {
        if (scopedServices == null)
            return;
        lock (StaticLock) {
            if (!ReferenceEquals(_scopedServices, scopedServices))
                return;

            _scopedServicesSource.TrySetCanceled();
            _scopedServicesSource = TaskCompletionSourceExt.New<IServiceProvider>(); // Must go first
            _scopedServices = null;
        }
        Log.LogWarning(
            "Active ScopedServices are discarded ({Reason}); stack trace:\n{StackTrace}",
            reason, Environment.StackTrace);
        Dispose(scopedServices);
    }

    private static void Dispose(IServiceProvider scopedServices)
    {
        try {
            if (scopedServices.GetService<SafeJSRuntime>() is { } safeJSRuntime)
                safeJSRuntime.MarkDisconnected();
        }
        catch {
            return; // Already disposed
        }
        _ = DelayedDispose(scopedServices);
    }

    private static async Task DelayedDispose(IServiceProvider scopedServices)
    {
        try {
            var cancellationToken = scopedServices.GetRequiredService<AppBlazorCircuitContext>().StopToken;
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) {
            Log.LogInformation($"{nameof(DelayedDispose)}: unnecessary");
            return; // Already disposed
        }
        _ = MainThread.InvokeOnMainThreadAsync(async () => {
            Log.LogInformation($"{nameof(DelayedDispose)}: started");
            if (scopedServices is IAsyncDisposable ad)
                await ad.DisposeSilentlyAsync().ConfigureAwait(false);
            else if (scopedServices is IDisposable d)
                d.DisposeSilently();
        });
    }
}
