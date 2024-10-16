using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualLab.Internal;

namespace ActualChat.App.Maui;

#pragma warning disable CA1052
#pragma warning disable CA1849 // 'Task<TResult>.Result' synchronously blocks. Use await instead.

public class AppServicesAccessor
{
    private static readonly TimeSpan WhenRenderedTimeout = TimeSpan.FromSeconds(3);

    private static readonly object AppServicesLock = new(); // Otherwise Rider assumes we're referencing it from elsewhere
    private static ILogger? _log; // Otherwise Rider assumes we're referencing it from elsewhere
    private static volatile IServiceProvider? _scopedServices;
    // ReSharper disable once InconsistentNaming
    private static readonly TaskCompletionSource<IServiceProvider> _appServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _blazorAppServicesSource =
        TaskCompletionSourceExt.New<IServiceProvider>();
    private static volatile TaskCompletionSource<IServiceProvider> _blazorAppServicesChangedSource =
        TaskCompletionSourceExt.New<IServiceProvider>();

    private static ILogger Log // Otherwise Rider assumes we're referencing it from elsewhere
        => _log ??= StaticLog.Factory.CreateLogger<AppServicesAccessor>();

    public static IServiceProvider BlazorAppServices {
        get => _scopedServices ?? throw Errors.NotInitialized(nameof(BlazorAppServices));
        set {
            lock (AppServicesLock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_scopedServices, value))
                    return;
                if (_scopedServices != null)
                    TryDiscardActiveBlazorAppServices(_scopedServices, "BlazorAppServices.set");

                _scopedServices = value;
                _blazorAppServicesSource.TrySetResult(value);
                _blazorAppServicesChangedSource.TrySetResult(value);
                _blazorAppServicesChangedSource = TaskCompletionSourceExt.New<IServiceProvider>();
                Log.LogDebug("BlazorAppServices ready");
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

    public static Task<IServiceProvider> WhenBlazorAppServicesChanged(CancellationToken cancellationToken = default)
        => _blazorAppServicesChangedSource.Task.WaitAsync(cancellationToken);
    public static Task<IServiceProvider> WhenBlazorAppServicesReady(CancellationToken cancellationToken = default)
        => WhenBlazorAppServicesReady(false, cancellationToken);
    public static Task<IServiceProvider> WhenBlazorAppServicesReady(
        bool whenRendered, CancellationToken cancellationToken = default)
    {
        var scopedServicesTask = _blazorAppServicesSource.Task;
        if (scopedServicesTask.IsCompletedSuccessfully) {
            var c = scopedServicesTask.Result;
            var circuitContext = c.GetRequiredService<AppBlazorCircuitContext>();
            if (circuitContext.WhenReady.IsCompletedSuccessfully) {
                if (!whenRendered)
                    return scopedServicesTask;

                var loadingUI = c.GetRequiredService<LoadingUI>();
                if (loadingUI.WhenRendered.IsCompletedSuccessfully)
                    return scopedServicesTask;
            }
        }
        return WhenBlazorAppServicesReadyAsync(whenRendered, cancellationToken);
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

    public static void TryDiscardActiveBlazorAppServices(IServiceProvider? scopedServices, string reason)
    {
        if (scopedServices == null)
            return;
        lock (AppServicesLock) {
            if (!ReferenceEquals(_scopedServices, scopedServices))
                return;

            _blazorAppServicesSource.TrySetCanceled();
            _blazorAppServicesSource = TaskCompletionSourceExt.New<IServiceProvider>(); // Must go first
            _scopedServices = null;
        }
        Log.LogInformation("Active BlazorAppServices are discarded ({Reason})", reason);
        EnsureDisposed(scopedServices);
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
                var circuitContext = c.GetRequiredService<AppBlazorCircuitContext>();
                await circuitContext.WhenReady
                    .WaitAsync(WhenRenderedTimeout, cancellationToken)
                    .SilentAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (circuitContext.WhenReady.IsCompletedSuccessfully)
                    return c;
            }
        }
    }

    private static void EnsureDisposed(IServiceProvider scopedServices)
    {
        try {
            if (scopedServices.GetService<SafeJSRuntime>() is { } safeJSRuntime)
                safeJSRuntime.MarkDisconnected();
        }
        catch {
            return; // Already disposed
        }
        _ = DelayedEnsureDisposed(scopedServices);
    }

    private static async Task DelayedEnsureDisposed(IServiceProvider scopedServices)
    {
        try {
            var cancellationToken = scopedServices.GetRequiredService<AppBlazorCircuitContext>().StopToken;
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) {
            return; // Already disposed
        }
        Log.LogWarning("ScopedServices aren't disposed in 15 seconds!");
    }
}
