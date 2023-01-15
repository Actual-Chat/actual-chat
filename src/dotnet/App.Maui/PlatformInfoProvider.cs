using Platform = ActualChat.Hosting.Platform;
namespace ActualChat.App.Maui;

public static class PlatformInfoProvider
{
    public static Platform GetPlatform()
    {
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            return Platform.Android;

        if (DeviceInfo.Current.Platform == DevicePlatform.iOS)
            return Platform.iOS;

        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            return Platform.Windows;

        if (DeviceInfo.Current.Platform == DevicePlatform.macOS)
            return Platform.MacOS;

        return Platform.Unknown;
    }
}
