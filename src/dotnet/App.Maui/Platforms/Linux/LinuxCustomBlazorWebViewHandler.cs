using Microsoft.AspNetCore.Components;
using Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace ActualChat.App.Maui;

/// <summary>
/// GTK counterpart of <see cref="CustomBlazorWebViewHandler"/>: swaps the MAUI context to the
/// Blazor app's services, and puts its own WebKit view into the labs handler's box - the labs
/// handler supplies the GTK layout plumbing, <see cref="LinuxWebViewManager"/> the Blazor part.
/// </summary>
public sealed class LinuxCustomBlazorWebViewHandler : BlazorWebViewHandler
{
    private WebKit.WebView? _webView;
    private LinuxWebViewManager? _webViewManager;

    private ILogger Log => field ??= StaticLog.For(GetType());

    public override void SetMauiContext(IMauiContext mauiContext)
    {
        var services = BlazorWebViewApp.WaitForServices(Log);
        base.SetMauiContext(new GtkMauiContext(services));
    }

    protected override Gtk.Box CreatePlatformView()
    {
        // The base method isn't called: it builds the labs GtkBlazorWebView, see LinuxWebViewManager.
        // Without one the labs property mappers are no-ops, and ConnectHandler does their job.
        GtkBlazorWebView.InitializeWebKit();
        _webView = new WebKit.WebView();
        _webView.SetHexpand(true);
        _webView.SetVexpand(true);
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        box.SetHexpand(true);
        box.SetVexpand(true);
        box.Append(_webView);
        return box;
    }

    protected override void ConnectHandler(Gtk.Box platformView)
    {
        base.ConnectHandler(platformView);

        var webView = _webView!;
        MauiWebView.Current?.SetPlatformWebView(webView);
        var webViewManager = new LinuxWebViewManager(
            webView, MauiContext!.Services, VirtualView.JSComponents, VirtualView.HostPage!);
        _webViewManager = webViewManager;
        foreach (var rootComponent in VirtualView.RootComponents) {
            var parameters = rootComponent.Parameters is { } p
                ? ParameterView.FromDictionary(p)
                : ParameterView.Empty;
            _ = webViewManager.AddRootComponentAsync(rootComponent.ComponentType!, rootComponent.Selector!, parameters);
        }
        webViewManager.Navigate(VirtualView.StartPath);
    }

    protected override void DisconnectHandler(Gtk.Box platformView)
    {
        // Not awaited here: the renderer's disposal may need the UI thread this method holds
        if (_webViewManager is { } webViewManager)
            _ = BackgroundTask.Run(
                () => webViewManager.DisposeAsync().AsTask(),
                Log, "WebViewManager disposal failed");
        _webViewManager = null;
        if (_webView is { } webView) {
            // Dropping the widget isn't enough: the page lives in a web process of its own, which
            // keeps running its scripts - timers, the audio context, RPC - for as long as anything
            // still references the view
            webView.TerminateWebProcess();
            platformView.Remove(webView);
            webView.Dispose();
        }
        _webView = null;
        base.DisconnectHandler(platformView);
    }
}
