using ActualChat.Maui.Services;
using AppKit;
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
    private static NSWindow? _window;
    private static WindowDelegate? _windowDelegate;
    private static WKWebView? _webView;
    private static bool _isFullScreen;
    private static NSObject? _mouseMonitor;
    private static NSEvent? _lastMouseEvent;

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

    // Injected at document start too, so a reload reserves the strip from its first paint
    public static string GetTitlebarInsetScript()
        => $"document.documentElement.style.setProperty('--titlebar-inset', '{GetTitlebarInset():0.##}px')";

    // Private methods

    private static void PushTitlebarInset()
        => _ = _webView?.EvaluateJavaScriptAsync(GetTitlebarInsetScript());

    private static double GetTitlebarInset()
    {
        if (Window is not { } window || _isFullScreen)
            return 0;

        return window.Frame.Height - window.ContentLayoutRect.Height;
    }

    [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend(nint receiver, nint selector, NFloat value);

    // Nested types

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
            _isFullScreen = true;
            PushTitlebarInset();
        }

        public override void DidExitFullScreen(NSNotification notification)
        {
            _isFullScreen = false;
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
