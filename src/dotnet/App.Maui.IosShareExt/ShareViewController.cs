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
        if (_app == null)
            return;

        View = new ShareView(_app.Services.IosHub());
    }
}
