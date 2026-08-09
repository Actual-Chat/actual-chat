using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt;

[Register("ShareViewController")]
[SuppressMessage("Design", "CA1010:Generic interface should also be implemented")]
public class ShareViewController : UIViewController
{
    private ShareExtensionApplication? _app;

    public override void LoadView()
    {
        _app = ShareExtensionApplication.Bootstrap(this);
        // Leaving View unset makes UIKit throw NSInvalidArgumentException ("attempt to insert nil
        // object"), so a failed bootstrap used to crash the extension outright - which also killed
        // the failure report Bootstrap had just started sending.
        View = _app is null
            ? new ErrorContentView("Something went wrong. Please try again.",
                (_, _) => _ = ExtensionContext?.CompleteRequestAsync([]))
            : new ShareView(_app.Services.IosHub());
    }
}
