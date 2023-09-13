using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.App.Maui;

/// <summary>
/// Prevents rendering MauiBlazorApp after WebView was marked as disconnected.
/// </summary>
/// <remarks>
/// After replacing WebView with a new one sometimes hard reload happens on a WebView at address '/chat/' on a replaced WebView.
/// This causes creating a new instance of MauiBlazorApp and setting ScopedServices.
/// In moment later new WebView will also load MauiBlazorApp which will try to set ScopedServices and will fail.
/// This failure will launch app reloading and this may repeat many times.
/// Preventing rendering MauiBlazorApp on disconnected WebView prevents conflicts in setting ScopedServices.
/// </remarks>
public class MauiBlazorAppWrapper : ComponentBase
{
    private bool _shouldRender;
    private bool _rendered;

    [Inject] private ILogger<MauiBlazorAppWrapper> Log { get; set; } = null!;
    [Parameter] public BlazorWebViewDisconnectMarker DisconnectMarker { get; set; } = null!;

    protected override void OnInitialized()
    {
        _shouldRender = !DisconnectMarker.IsDisconnected;
        if (_shouldRender)
            _ = MonitorDisconnection();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_shouldRender) {
            builder.OpenComponent<MauiBlazorApp>(0);
            builder.CloseComponent();
            _rendered = true;
        }
        else
            _rendered = false;
    }

    private async Task MonitorDisconnection()
    {
        await DisconnectMarker.WhenDisconnected.ConfigureAwait(false);
        // Wait a while to prevent using resources during loading new WebView and new MauiBlazorApp.
        await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        Log.LogInformation("BlazorWebView cleaning routine started");
        await InvokeAsync(() => {
            if (_rendered) {
                // BlazorWebView.Handler.DisconnectHandler call may cause deadlock if WebViewRenderer contains components which
                // implements IAsyncDisposable.
                // So to prevent deadlock Unload inner components to ensure WebViewRenderer dispose components.
                _shouldRender = false;
                StateHasChanged();
            }
        }).ConfigureAwait(false);
        // Wait a while till WebViewRenderer finish disposing components
        await Task.Delay(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        var disconnectTask = MainThread.InvokeOnMainThreadAsync(() => {
            try {
                DisconnectMarker.BlazorWebView.Handler?.DisconnectHandler();
                Log.LogInformation("DisconnectHandler completed successfully");
            }
            catch (Exception e) {
                Log.LogWarning(e, "An error occurred during invoking DisconnectHandler");
            }
        });
        try {
            await disconnectTask.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("DisconnectHandler did not completed within 15 seconds");
        }
        // This should release as much resources as possible, but it seems this not enough:
        // On Windows in memory profiler I still can see old WebView instances are available.
        // On Android in developer tools I still can see old WebView is listed as detached.
    }
}
