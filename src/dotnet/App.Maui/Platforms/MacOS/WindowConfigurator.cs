using ActualChat.Maui.Services;
using AppKit;
using Foundation;
using ObjCRuntime;

namespace ActualChat.App.Maui;

/// <summary>
/// Desktop window policy of the AppKit app, the twin of the Windows <c>WindowConfigurator</c>:
/// the red button hides the window instead of closing it while <see cref="App.MustMinimizeOnQuit"/>
/// holds, a Dock click brings it back, and the background state follows window visibility.
/// </summary>
internal static class WindowConfigurator
{
    private static NSWindow? _window;
    private static WindowDelegate? _windowDelegate;

    public static void Configure()
    {
        if (App.Current.Windows.FirstOrDefault()?.Handler?.PlatformView is not NSWindow window)
            return;

        _window = window;
        _windowDelegate = new WindowDelegate(window.Delegate as NSWindowDelegate);
        window.Delegate = _windowDelegate;
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

    public static void KeepTitlebarAboveContent(NSWindow window)
    {
        // The labs BlazorWebViewHandler adds a titlebar-wide drag overlay above every other frame
        // view, traffic lights included, so their clicks start a window drag instead. AppKit itself
        // puts the titlebar back on top after any full-screen round trip; this does it right away.
        // TODO(maui-labs): delete once TitlebarDragOverlayView.HitTest skips the standard window buttons.
        var themeFrame = window.ContentView?.Superview;
        var subviews = themeFrame?.Subviews ?? [];
        var titlebar = subviews.FirstOrDefault(v => v.Class.Name == "NSTitlebarContainerView");
        if (titlebar == null || titlebar.Handle == subviews[^1].Handle)
            return;

        themeFrame!.AddSubview(titlebar, NSWindowOrderingMode.Above, null);
    }

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
    }
}
