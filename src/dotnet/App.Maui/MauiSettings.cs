using ActualChat.Hosting;

namespace ActualChat.App.Maui;

public static class MauiSettings
{
    public const string LocalHost = "0.0.0.0";
#if IS_DEV_MAUI
    public const bool IsDevApp = true;
#else
    public const bool IsDevApp = false;
#endif
    public const string Host = IsDevApp ? "dev.actual.chat" : "actual.chat";
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly AppKind AppKind;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0036A3");

    static MauiSettings()
    {
        BaseUrl = "https://" + Host + "/";
        BaseUri = BaseUrl.ToUri();

#if ANDROID
        AppKind = AppKind.Android;
#elif WINDOWS
        AppKind = AppKind.Windows;
#elif MACCATALYST
        AppKind = AppKind.MacOS;
#elif IOS
        AppKind = AppKind.Ios;
#else
        AppKind = AppKind.Unknown;
#endif
    }

    // Nested types

    public static class WebAuth
    {
        public static readonly bool UseSystemBrowser = true;
    }
}
