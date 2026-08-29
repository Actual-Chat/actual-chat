using ActualChat.UI.Blazor;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiWebViewPageContextTracker))]
public sealed class MauiWebViewPageContextTracker(IServiceProvider services) : IDisposable
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<MauiWebViewPageContextTracker>();
    private readonly Lock _lock = new();
    private readonly UIHub _hub = services.GetRequiredService<UIHub>();
    private readonly SafeJSRuntime _safeJSRuntime = services.GetRequiredService<SafeJSRuntime>();
    private MauiWebView? _mauiWebView;
    private bool _disconnectJSRuntimeRequested;
    private bool _monitorDisposeCompletionRequested;
#if DEBUG
    private IAsyncDisposable? _pageContext;
#endif

    public void AttachTo(MauiWebView? mauiWebView)
    {
        if (mauiWebView is null)
            Log.LogWarning("AttachTo is invoked with MauiWebView <null>");
        if (_mauiWebView is not null)
            Log.LogWarning("Tracker has been already attached to MauiWebView #{Id}", _mauiWebView.Id);
        _mauiWebView = mauiWebView;
        if (_mauiWebView is null)
            return;

        _mauiWebView.LinkTo(this);
#if DEBUG
        if (_mauiWebView.BlazorWebView.Handler is BlazorWebViewHandler blazorWebViewHandler) {
            var webViewManager = blazorWebViewHandler.GetWebViewManager();
            _pageContext = webViewManager?.GetCurrentPageContext();
        }
#endif
    }

    public void OnMauiBlazorAppDisposing()
        => DisconnectJSRuntime();

    public void OnDiscardingActiveBlazorAppServices()
    {
        DisconnectJSRuntime();
        _ = MonitorDisposeCompletion();
    }

    public void OnMauiWebViewDisconnect()
        => DisconnectJSRuntime();

    public void Dispose()
    {
        // This service is one of the first to resolve in the scope.
        // And therefore will be one of the last to be disposed.
        // Launch dispose completion tracking from here.
        _ = MonitorDisposeCompletion();
        Log.LogDebug("Dispose; stack trace:\n{StackTrace}", Environment.StackTrace);
    }

    private void DisconnectJSRuntime([CallerMemberName] string reason = "")
    {
        Log.LogDebug("-> DisconnectJSRuntime. Reason: '{Reason}'", reason);
        lock (_lock) {
            if (_disconnectJSRuntimeRequested) {
                Log.LogDebug("DisconnectJSRuntime has been already invoked earlier");
                return;
            }

            _disconnectJSRuntimeRequested = true;
        }

        try {
            _safeJSRuntime.MarkDisconnected();
        }
        catch {
            // Already disposed
        }
    }

    private async Task MonitorDisposeCompletion([CallerMemberName] string reason = "")
    {
        Log.LogDebug("-> MonitorDisposeCompletion. Reason: '{Reason}'", reason);
        lock (_lock) {
            if (_monitorDisposeCompletionRequested) {
                Log.LogDebug("MonitorDisposeCompletion has been already invoked earlier");
                return;
            }

            _monitorDisposeCompletionRequested = true;
        }

        var cancellationToken = _hub.StopToken;
        try {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            if (cancellationToken.IsCancellationRequested) {
                Log.LogDebug("ScopedServices have been disposed");
#if DEBUG
                // Not disposed here: MAUI disposes it on reload, and it isn't idempotent.
                Log.LogDebug(_pageContext is not null ? "PageContext is captured" : "No _pageContext");
#endif
                return;
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Unexpected exception during monitoring dispose completion");
            return;
        }

        Log.LogWarning("ScopedServices aren't disposed in 15 seconds!");
    }
}
