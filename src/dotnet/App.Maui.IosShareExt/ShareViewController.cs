using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt;

[Register("ShareViewController")]
[SuppressMessage("Design", "CA1010:Generic interface should also be implemented")]
public class ShareViewController : UIViewController
{
    private ShareExtensionApplication? _app;

    public override void LoadView()
    {
        ApplyTheme();
        _app = ShareExtensionApplication.Bootstrap(this);
        // Leaving View unset makes UIKit throw NSInvalidArgumentException ("attempt to insert nil
        // object"), so a failed bootstrap used to crash the extension outright - which also killed
        // the failure report Bootstrap had just started sending.
        View = _app is null
            ? new ErrorContentView("Something went wrong. Please try again.",
                (_, _) => _ = ExtensionContext?.CompleteRequestAsync([]))
            : _app.View;
    }

    // Private methods

    private void ApplyTheme()
    {
        // Runs ahead of Bootstrap, i.e. ahead of Sentry and the DI container, so a failure here
        // can only be swallowed - and the system appearance it falls back to is what the
        // extension used before the App Group carried the theme.
        try {
            OverrideUserInterfaceStyle = AppColors.UserInterfaceStyle;
        }
        catch (Exception e) {
            new OSLogLogger(nameof(ShareViewController)).LogWarning(e, "Failed to apply the app's theme");
        }
    }
}
