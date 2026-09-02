using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
#if MACOS
// MAUI Essentials' MainThread is its "not implemented" neutral build on the macos TFM
using MainThread = ActualChat.App.Maui.MacOSMainThread;
#endif

namespace ActualChat.App.Maui;

#pragma warning disable CA1052
#pragma warning disable CA1849 // 'Task<TResult>.Result' synchronously blocks. Use await instead.

public class AppServicesAccessor
{
    private static readonly TimeSpan WhenRenderedTimeout = TimeSpan.FromSeconds(3);

    private static readonly Lock AppServicesLock = new(); // Otherwise, Rider assumes we're referencing it from elsewhere
    private static ILogger? _log; // Otherwise, Rider assumes we're referencing it from elsewhere
    private static volatile ScopeServicesInfo? _scopedServicesInfo;
    // ReSharper disable once InconsistentNaming
    private static readonly TaskCompletionSource<IServiceProvider> _appServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _blazorAppServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _blazorAppServicesChangedSource =
        TaskCompletionSourceExt.New<IServiceProvider>();

    private static ILogger Log // Otherwise, Rider assumes we're referencing it from elsewhere
        => _log ??= StaticLog.Factory.CreateLogger<AppServicesAccessor>();

#pragma warning disable CA1044 // Properties should not be write only
    public static IServiceProvider BlazorAppServices {
#pragma warning restore CA1044
        set {
            lock (AppServicesLock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_scopedServicesInfo?.Services, value))
                    return;

                if (_scopedServicesInfo is not null)
                    DiscardBlazorAppServices(_scopedServicesInfo.Services, "BlazorAppServices.set");

                var tracker = value.GetRequiredService<MauiWebViewPageContextTracker>();
                _scopedServicesInfo = new ScopeServicesInfo(value, tracker);
                _blazorAppServicesSource.TrySetResult(value);
                _blazorAppServicesChangedSource.TrySetResult(value);
                _blazorAppServicesChangedSource = TaskCompletionSourceExt.New<IServiceProvider>();
                Log.LogDebug("BlazorAppServices ready");
            }
        }
    }

    public static void DiscardBlazorAppServices(IServiceProvider serviceProvider, string reason)
    {
        Log.LogDebug("-> DiscardBlazorAppServices");
        lock (AppServicesLock) {
            if (_scopedServicesInfo == null)
                return;

            if (!ReferenceEquals(_scopedServicesInfo.Services, serviceProvider))
                return;

            var contextTracker = _scopedServicesInfo.ContextTracker;
            _blazorAppServicesSource = TaskCompletionSourceExt.New<IServiceProvider>(); // Must go first
            _scopedServicesInfo = null;
            Log.LogInformation("Active BlazorAppServices are discarded ({Reason})", reason);
            contextTracker.OnDiscardingActiveBlazorAppServices();
        }
        Log.LogDebug("-> DiscardBlazorAppServices");
    }

    public static bool TryGetScopedServices([NotNullWhen(true)] out IServiceProvider? scopedServices)
    {
        scopedServices = _scopedServicesInfo?.Services;
        return scopedServices != null;
    }

    public static Task<IServiceProvider> WhenAppServicesReady(CancellationToken cancellationToken = default)
        => _appServicesSource.Task.WaitAsync(cancellationToken);

    public static Task<IServiceProvider> WhenBlazorAppServicesChanged(CancellationToken cancellationToken = default)
        => _blazorAppServicesChangedSource.Task.WaitAsync(cancellationToken);
    public static Task<IServiceProvider> WhenBlazorAppServicesReady(CancellationToken cancellationToken = default)
        => WhenBlazorAppServicesReady(false, cancellationToken);
    public static Task<IServiceProvider> WhenBlazorAppServicesReady(
        bool whenRendered, CancellationToken cancellationToken = default)
    {
        var scopedServicesTask = _blazorAppServicesSource.Task;
        if (scopedServicesTask.IsCompletedSuccessfully) {
            var c = scopedServicesTask.GetAwaiter().GetResult();
            var hub = c.GetRequiredService<UIHub>();
            if (hub.WhenInitialized.IsCompletedSuccessfully) {
                if (!whenRendered)
                    return scopedServicesTask;

                var loadingUI = c.GetRequiredService<LoadingUI>();
                if (loadingUI.WhenRendered.IsCompletedSuccessfully)
                    return scopedServicesTask;
            }
        }
        return WhenBlazorAppServicesReadyAsync(whenRendered, cancellationToken);
    }

    // DispatchToMainThread

    public static void BeginDispatchToMainThread(Action action, bool allowInline = true)
    {
        if (!allowInline && MainThread.IsMainThread)
            _ = Task.Run(() => MainThread.BeginInvokeOnMainThread(action));
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    public static Task DispatchToMainThread(Action action, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(action))
            : MainThread.InvokeOnMainThreadAsync(action);

    public static Task<T> DispatchToMainThread<T>(Func<T> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);

    public static Task DispatchToMainThread(Func<Task> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);

    public static Task<T> DispatchToMainThread<T>(Func<Task<T>> func, bool allowInline = true)
        => !allowInline && MainThread.IsMainThread
            ? Task.Run(() => MainThread.InvokeOnMainThreadAsync(func))
            : MainThread.InvokeOnMainThreadAsync(func);

    // DispatchToBlazor

    public static Task DispatchToBlazor(Action<IServiceProvider> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => _ = ForegroundTask.Run(() => {
            workItem.Invoke(c);
            return Task.CompletedTask;
        }, Log, $"{name} failed"), whenRendered);

    public static Task DispatchToBlazor(Func<IServiceProvider, Task> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => ForegroundTask.Run(
            async () => await workItem.Invoke(c).ConfigureAwait(false),
            Log, $"{name} failed"), whenRendered);

    public static Task DispatchToBlazor<T>(Func<IServiceProvider, Task<T>> workItem, string name, bool whenRendered = false)
        => DispatchToBlazor(c => ForegroundTask.Run(
            async () => await workItem.Invoke(c).ConfigureAwait(false),
            Log, $"{name} failed"), whenRendered);

    public static async Task DispatchToBlazor(Action<IServiceProvider> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenBlazorAppServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        await dispatcher.InvokeSafeAsync(() => workItem.Invoke(scopedServices), Log).ConfigureAwait(false);
    }

    public static async Task DispatchToBlazor(Func<IServiceProvider, Task> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenBlazorAppServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        await dispatcher.InvokeAsync(() => workItem.Invoke(scopedServices)).ConfigureAwait(false);
    }

    public static async Task<T> DispatchToBlazor<T>(Func<IServiceProvider, Task<T>> workItem, bool whenRendered = false)
    {
        var scopedServices = await WhenBlazorAppServicesReady(whenRendered).ConfigureAwait(false);
        var dispatcher = scopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
        return await dispatcher.InvokeAsync(() => workItem.Invoke(scopedServices)).ConfigureAwait(false);
    }

    // Private methods

    private static async Task<IServiceProvider> WhenBlazorAppServicesReadyAsync(
        bool whenRendered, CancellationToken cancellationToken)
    {
        var mustWait = false;
        while (true) {
            if (mustWait) // We don't want to rapidly cycle here
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            mustWait = true;

            var scopedServicesTask = _blazorAppServicesSource.Task;
            await scopedServicesTask.SilentAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!scopedServicesTask.IsCompletedSuccessfully || scopedServicesTask.GetAwaiter().GetResult() is not { } c)
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
                var hub = c.GetRequiredService<UIHub>();
                await hub.WhenInitialized
                    .WaitAsync(WhenRenderedTimeout, cancellationToken)
                    .SilentAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (hub.WhenInitialized.IsCompletedSuccessfully)
                    return c;
            }
        }
    }

    private record ScopeServicesInfo(IServiceProvider Services, MauiWebViewPageContextTracker ContextTracker);
}
