using ActualChat.App.Maui.IosShareExt.Components;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Maui;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.App.Maui.IosShareExt;

[Register("ShareViewController")]
public class ShareViewController : UIViewController
{
    private static readonly ILogger Log = new OSLogLogger(nameof(ShareViewController));

    private ServiceProvider Services { get; set; } = null!;

    public override void LoadView()
    {
        Initialize();
        View = new ShareView(Services.IosHub());
    }

    private void Initialize()
    {
        try {
            Platform.Init(() => this);
            Services = App.Bootstrap();
        }
        catch (Exception e)
        {
            Log.LogCritical(e, "Failed to bootstrap the app");
        }
    }
}
