using ActualChat.Hosting;

namespace ActualChat.App.Maui;

public static class MauiSettings
{
    public const string LocalHost = "0.0.0.0";
#if IS_DEV_MAUI
    public const string Host = "dev.actual.chat";
#else
    public const string Host = "actual.chat";
#endif
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly ClientKind ClientKind;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0036A3");

    static MauiSettings()
    {
        BaseUrl = "https://" + Host + "/";
        BaseUri = BaseUrl.ToUri();

        var platform = DeviceInfo.Current.Platform;
        if (platform == DevicePlatform.Android)
            ClientKind = ClientKind.Android;
        else if (platform == DevicePlatform.iOS)
            ClientKind = ClientKind.Ios;
        else if (platform == DevicePlatform.WinUI)
            ClientKind = ClientKind.Windows;
        else if (platform == DevicePlatform.macOS)
            ClientKind = ClientKind.MacOS;
        else
            ClientKind = ClientKind.Unknown;
    }

    // Nested types

    public static class WebAuth
    {
        public static readonly bool UseSystemBrowser = true;
    }
}
