using ActualChat.UI.Blazor.App.Services.Video;
using CoreGraphics;
using UIKit;

namespace ActualChat.App.Maui.Video;

/// <summary>
/// Hosts native <see cref="SampleBufferDisplayView"/>s in a container pinned over the
/// WKWebView so each remote video tile is drawn natively while its chrome stays in the DOM.
/// The JS rect tracker reports each tile's viewport rect (CSS px, == webview points) through
/// <see cref="VideoTrackPlayer"/> into <see cref="UpdateRect"/>; the matching layer view is
/// moved to that frame. Layer views are added by the native player via <see cref="Attach"/>.
/// </summary>
public sealed class NativeVideoOverlayHost : INativeVideoOverlayHost
{
    // AboveWebView covers the tile regardless of DOM/webview transparency — the reliable
    // bring-up default. Switch to BelowWebView (underlay) once the tile+ancestor
    // backgrounds are confirmed transparent on Mac Catalyst, so DOM chrome (labels, menus,
    // modals) composites correctly on top. See docs/plans/maccatalyst-native-video-overlay.md.
    private enum Placement { AboveWebView, BelowWebView }
    private static readonly Placement OverlayPlacement = Placement.AboveWebView;

    private readonly ILogger _log;
    private readonly Dictionary<string, SampleBufferDisplayView> _views = new();
    private UIView? _container;

    public NativeVideoOverlayHost(ILogger log)
        => _log = log;

    internal void Attach(string id, SampleBufferDisplayView view)
        => OnMain(() => {
            var container = EnsureContainer();
            if (container is null)
                return;

            if (_views.Remove(id, out var existing) && !ReferenceEquals(existing, view))
                existing.RemoveFromSuperview();
            view.Hidden = true; // shown once the first rect arrives
            _views[id] = view;
            container.AddSubview(view);
        });

    public void UpdateRect(string id, double x, double y, double width, double height, bool visible, double cornerRadius)
        => OnMain(() => {
            if (!_views.TryGetValue(id, out var view))
                return;

            view.Hidden = !visible || width <= 0 || height <= 0;
            view.Frame = new CGRect(x, y, width, height);
            view.SetCornerRadius((nfloat)cornerRadius);
        });

    public void Remove(string id)
        => OnMain(() => {
            if (_views.Remove(id, out var view))
                view.RemoveFromSuperview();
        });

    // Private methods

    private UIView? EnsureContainer()
    {
        if (_container is not null)
            return _container;

        var webView = MauiWebView.Current?.WKWebView;
        var superview = webView?.Superview;
        if (webView is null || superview is null) {
            _log.LogWarning("NativeVideoOverlayHost: WKWebView/superview not available yet");
            return null;
        }

        var container = new UIView(webView.Frame) {
            UserInteractionEnabled = false,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
        };
        if (OverlayPlacement == Placement.BelowWebView)
            superview.InsertSubviewBelow(container, webView);
        else
            superview.InsertSubviewAbove(container, webView);
        _container = container;
        return container;
    }

    private static void OnMain(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }
}
