using UIKit;

namespace ActualChat.App.Maui;

public class IosBars : Bars
{
    public override int GetStatusBarHeight()
    {
        if (DeviceInfo.Version < new Version("16.4"))
            return 0;

 #pragma warning disable CA1422
        return (int)UIApplication.SharedApplication.StatusBarFrame.Height;
 #pragma warning restore CA1422
    }
}
