using ActualChat.App.Maui.Services;
using ActualChat.Hosting;

namespace ActualChat.App.Maui;

public static class MauiSettings
{
#if IOS
    public const string LocalHost = "localhost";
#else
    public const string LocalHost = "0.0.0.0";
#endif
#if IS_DEV_MAUI
    public const bool IsDevApp = true;
#else
    public const bool IsDevApp = false;
#endif
    public const string DefaultHost = IsDevApp ? "dev.actual.chat" : "actual.chat";
    public static readonly string Host;
    public static bool HostIsOverriden => !OrdinalIgnoreCaseEquals(Host, DefaultHost);
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly AppKind AppKind;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0036A3");

    static MauiSettings()
    {
        Host = GetHostOverride() ?? DefaultHost;
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

    private static string? GetHostOverride()
        => MauiHostSwitcher.GetHostOverride()?.Host;

    // Nested types

    public static class WebAuth
    {
        public static readonly bool UseSystemBrowser = true;
    }
}
