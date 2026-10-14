using ActualChat.Maui.Services;

namespace ActualChat.App.Maui;

/// <summary>
/// Desktop window policy of the GTK app, the twin of the Windows and AppKit
/// <c>WindowConfigurator</c>s: the window may shrink to the narrow layout, and the background
/// state follows whether the user can see it.
/// </summary>
internal static class WindowConfigurator
{
    private const int MinWidth = 360;
    private const int MinHeight = 480;
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 800;

    private static Gtk.Window? _window;

    public static bool IsWindowActive => _window?.GetIsActive() ?? false;

    public static void Configure()
    {
        if (App.Current.Windows.FirstOrDefault()?.Handler?.PlatformView is not Gtk.Window window)
            return;

        _window = window;
        // LinuxActivation's GApplication is created first, which makes it the default one, while
        // notifications and their actions go through the default one and belong to the backend's
        window.GetApplication()?.SetDefault();
        // TODO(maui-labs): drop once the backend stops pinning its window at 800x600 or more
        // (it does in this preview), which rules out the narrow layout.
        window.SetSizeRequest(MinWidth, MinHeight);
        window.SetDefaultSize(DefaultWidth, DefaultHeight);
        window.OnNotify += OnNotify;
        UpdateBackgroundState();
    }

    public static void ShowWindow()
        => _window?.Present();

    // Private methods

    private static void OnNotify(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "suspended")
            UpdateBackgroundState();
    }

    private static void UpdateBackgroundState()
    {
        // "Can the user see the window", not "is the app active", as in the AppKit app: the
        // compositor suspends a window that is minimized, fully covered or on another workspace.
        var isHidden = _window?.IsSuspended() ?? false;
        MauiBackgroundState.SetIsBackground(isHidden);
        MauiBackgroundState.SetIsHidden(isHidden);
    }
}
