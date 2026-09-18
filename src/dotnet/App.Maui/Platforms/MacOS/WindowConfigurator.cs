using ActualChat.Maui.Services;
using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using WebKit;

namespace ActualChat.App.Maui;

/// <summary>
/// Desktop window policy of the AppKit app, the twin of the Windows <c>WindowConfigurator</c>:
/// the red button hides the window instead of closing it while <see cref="App.MustMinimizeOnQuit"/>
/// holds, a Dock click brings it back, the background state follows window visibility, and the
/// web UI extends under the titlebar: the page reserves its height and marks its own drag regions.
/// </summary>
internal static partial class WindowConfigurator
{
    // Where a unified toolbar puts the traffic lights, 19pt from the corner (a bare titlebar puts them
    // 9pt in): clear of the rounded corner of a panel that fills the window
    private const double InsetTrafficLightsOffset = 19;

    private static NSWindow? _window;
    private static WindowDelegate? _windowDelegate;
    private static WKWebView? _webView;
    private static bool _isFullScreen;
    private static NSObject? _mouseMonitor;
    private static NSEvent? _lastMouseEvent;
    private static double? _trafficLightsOffset;
    private static double _defaultTrafficLightsOffset;
    private static double _trafficLightsSpacing;
    private static NSObject[] _trafficLightsObservers = [];
    private static NSButton? _observedCloseButton;
    private static bool _isLayingOutTrafficLights;

    public static IWKScriptMessageHandler WindowDragHandler { get; } = new WindowDragMessageHandler();
    private static NSWindow? Window
        => _window ??= App.Current.Windows.FirstOrDefault()?.Handler?.PlatformView as NSWindow;

    public static void Configure()
    {
        if (Window is not { } window)
            return;

        _isFullScreen = window.StyleMask.HasFlag(NSWindowStyle.FullScreenWindow);
        _windowDelegate = new WindowDelegate(window.Delegate as NSWindowDelegate);
        window.Delegate = _windowDelegate;
        // A window drag must start from the press that began it, and once the page's request
        // arrives CurrentEvent has often moved on - to a trackpad pressure event, typically
        _mouseMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.LeftMouseDown | NSEventMask.LeftMouseDragged,
            e => _lastMouseEvent = e);
        // The labs menu bar has no File menu, so Cmd+W lives in the Window menu
        if (NSApplication.SharedApplication.WindowsMenu is { } windowsMenu) {
            windowsMenu.InsertItem(NSMenuItem.SeparatorItem, 0);
            windowsMenu.InsertItem(new NSMenuItem("Close Window", new Selector("performClose:"), "w"), 0);
        }
        UpdateBackgroundState();
    }

    // Returns false when there is no window to show, i.e. AppKit's default reopen handling applies
    public static bool TryShowWindow()
    {
        if (_window is not { } window)
            return false;

        if (window.IsMiniaturized)
            window.Deminiaturize(null);
        else
            window.MakeKeyAndOrderFront(null);
        UpdateBackgroundState();
        return true;
    }

    public static void UpdateBackgroundState()
    {
        // Focus and visibility are the desktop's "is the user looking" signal: without them the app
        // counts as foreground forever, so an open chat keeps auto-reading incoming messages - which
        // also suppresses their notifications (the server hides read ones).
        var isVisible = _window is null or { IsVisible: true };
        MauiBackgroundState.Set(!NSApplication.SharedApplication.Active || !isVisible);
    }

    public static void RemoveTitlebarDragOverlay(NSWindow window)
    {
        // The labs BlazorWebViewHandler adds a titlebar-wide drag overlay above every other frame
        // view, traffic lights included: their clicks started a window drag, and with the page
        // extending under the titlebar the overlay would take every click in the headers' top strip
        // too. The page marks its own drag regions instead - see WindowDragHandler.
        // TODO(maui-labs): delete once the overlay is opt-out or hit-tests the page.
        var themeFrame = window.ContentView?.Superview;
        var overlay = themeFrame?.Subviews.FirstOrDefault(v => v.Class.Name?.EndsWith("TitlebarDragOverlayView") == true);
        overlay?.RemoveFromSuperview();
    }

    public static void ExtendContentUnderTitlebar(WKWebView webView)
    {
        // The labs handler insets the page by the titlebar height (setObscuredContentInsets) once the
        // WebView joins its window, and keeps the inset in full screen where the titlebar auto-hides,
        // so the strip under the transparent titlebar stays blank on every screen. With zero insets
        // the page paints edge to edge and reserves the traffic lights' corner itself, the way it
        // reserves the notch on iOS: the titlebar height goes out as --titlebar-inset.
        // TODO(maui-labs): delete once MacOSBlazorWebView.ContentInsets can opt out of the auto inset.
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            webView.ObscuredContentInsets = default;
        else
            objc_msgSend(webView.Handle, Selector.GetHandle("_setTopContentInset:"), 0);
        _webView = webView;
        PushTitlebarInset();
    }

    // Injected at document start too, so a reload reserves the strip from its first paint - hence on
    // <html>, the only element that exists by then. The class marks the host for the styles that
    // depend on the titlebar being there rather than on its height.
    public static string GetTitlebarInsetScript()
        => "document.documentElement.classList.add('native-titlebar');"
            + $"document.documentElement.style.setProperty('--titlebar-inset', '{GetTitlebarInset():0.##}px')";

    // A panel filling the window (the expanded call) would run its rounded corner under the traffic
    // lights, so while one is open they move further in; otherwise AppKit lays them out as usual
    public static void SetInsetTrafficLights(bool mustInset)
    {
        double? offset = mustInset ? InsetTrafficLightsOffset : null;
        if (offset == _trafficLightsOffset)
            return;

        _trafficLightsOffset = offset;
        if (!_isFullScreen && GetTrafficLights() is { } lights) {
            ObserveTrafficLights(lights);
            PlaceTrafficLights(lights, offset ?? _defaultTrafficLightsOffset);
        }
        PushTitlebarInset();
    }

    // Private methods

    private static void PushTitlebarInset()
        => _ = _webView?.EvaluateJavaScriptAsync(GetTitlebarInsetScript());

    private static double GetTitlebarInset()
    {
        if (Window is not { } window || _isFullScreen)
            return 0;
        if (_trafficLightsOffset is { } offset && GetTrafficLights() is { } lights)
            return GetTitlebarHeight(lights, offset);

        return window.Frame.Height - window.ContentLayoutRect.Height;
    }

    private static void LayoutTrafficLights()
    {
        // AppKit lays the buttons out again on a resize, a focus change and more; full screen moves
        // them into its own auto-hiding strip
        if (_trafficLightsOffset is not { } offset || _isFullScreen)
            return;
        if (_isLayingOutTrafficLights || GetTrafficLights() is not { } lights)
            return;

        ObserveTrafficLights(lights);
        PlaceTrafficLights(lights, offset);
    }

    private static TrafficLights? GetTrafficLights()
    {
        if (Window is not { } window)
            return null;
        if (window.StandardWindowButton(NSWindowButton.CloseButton) is not { Superview.Superview: { } container } close)
            return null;

        NSButton[] buttons = [
            close,
            window.StandardWindowButton(NSWindowButton.MiniaturizeButton),
            window.StandardWindowButton(NSWindowButton.ZoomButton),
        ];
        return new TrafficLights(window, container, buttons);
    }

    private static void ObserveTrafficLights(TrafficLights lights)
    {
        // Frame changes catch every relayout that puts the buttons back, not just the ones a window
        // notification announces
        var buttons = lights.Buttons;
        if (ReferenceEquals(buttons[0], _observedCloseButton))
            return;

        _observedCloseButton = buttons[0];
        // Read before the first move, while AppKit's own layout is still in place
        _defaultTrafficLightsOffset = buttons[0].Frame.X;
        _trafficLightsSpacing = buttons[1].Frame.X - buttons[0].Frame.X;
        foreach (var observer in _trafficLightsObservers)
            observer.Dispose();
        NSView[] views = [lights.Container, ..buttons];
        foreach (var view in views)
            view.PostsFrameChangedNotifications = true;
        _trafficLightsObservers = [
            ..views.Select(view => NSNotificationCenter.DefaultCenter.AddObserver(
                NSView.FrameChangedNotification, _ => LayoutTrafficLights(), view)),
        ];
    }

    private static void PlaceTrafficLights(TrafficLights lights, double offset)
    {
        _isLayingOutTrafficLights = true;
        try {
            // The titlebar container clips the buttons, so it takes the titlebar height first
            SetContainerHeight(lights, GetTitlebarHeight(lights, offset));
            for (var i = 0; i < lights.Buttons.Length; i++) {
                var button = lights.Buttons[i];
                button.SetFrameOrigin(GetTrafficLightOrigin(button, i, offset));
            }
        }
        finally {
            _isLayingOutTrafficLights = false;
        }
    }

    private static void SetContainerHeight(TrafficLights lights, double height)
    {
        var frame = lights.Container.Frame;
        lights.Container.Frame = new CGRect(frame.X, lights.Window.Frame.Height - height, frame.Width, height);
    }

    private static CGPoint GetTrafficLightOrigin(NSButton button, int index, double offset)
    {
        // The offset runs from the window's top-left corner, the way AppKit places the buttons
        var x = offset + (index * _trafficLightsSpacing);
        if (button.Superview is not { IsFlipped: false } superview)
            return new CGPoint(x, offset);

        return new CGPoint(x, superview.Frame.Height - offset - button.Frame.Height);
    }

    private static double GetTitlebarHeight(TrafficLights lights, double offset)
        => (offset * 2) + lights.Buttons[0].Frame.Height;

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend(nint receiver, nint selector, NFloat value);

    // Nested types

    private sealed record TrafficLights(NSWindow Window, NSView Container, NSButton[] Buttons);

    private sealed class WindowDelegate(NSWindowDelegate? inner) : NSWindowDelegate
    {
        public override bool WindowShouldClose(NSObject sender)
        {
            if (!App.MustMinimizeOnQuit)
                return inner?.WindowShouldClose(sender) ?? true;

            (sender as NSWindow)?.OrderOut(null);
            UpdateBackgroundState();
            return false;
        }

        public override void WillClose(NSNotification notification)
            => inner?.WillClose(notification);

        public override void DidMiniaturize(NSNotification notification)
            => UpdateBackgroundState();

        public override void DidDeminiaturize(NSNotification notification)
            => UpdateBackgroundState();

        public override void WillEnterFullScreen(NSNotification notification)
        {
            // Full screen takes the buttons over from AppKit's own layout
            if (_trafficLightsOffset is not null && GetTrafficLights() is { } lights)
                PlaceTrafficLights(lights, _defaultTrafficLightsOffset);
            _isFullScreen = true;
            PushTitlebarInset();
        }

        public override void DidExitFullScreen(NSNotification notification)
        {
            _isFullScreen = false;
            LayoutTrafficLights();
            PushTitlebarInset();
        }
    }

    private sealed class WindowDragMessageHandler : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            // Sent by window-drag.ts on a press that moved (drag) or a double click (zoom) in a page
            // region marked data-window-drag
            if (Window is not { } window)
                return;

            switch (message.Body.ToString()) {
            case "drag":
                if (_lastMouseEvent is { } mouseEvent)
                    window.PerformWindowDrag(mouseEvent);
                break;
            case "zoom":
                switch (NSUserDefaults.StandardUserDefaults.StringForKey("AppleActionOnDoubleClick")) {
                case "Minimize":
                    window.Miniaturize(null);
                    break;
                case "None":
                    break;
                default:
                    window.PerformZoom(null);
                    break;
                }
                break;
            }
        }
    }
}
