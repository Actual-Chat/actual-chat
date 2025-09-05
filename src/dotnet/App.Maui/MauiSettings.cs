using ActualChat.App.Maui.Services;
using ActualChat.Hosting;

namespace ActualChat.App.Maui;

public static class MauiSettings
{
    public static readonly string LocalHost = "0.0.0.1";
#if IS_DEV_MAUI
    public const bool IsDevApp = true;
    public const bool AreDevToolsEnabled = true;
#else
    public const bool IsDevApp = false;
#if DEBUG
    public const bool AreDevToolsEnabled = true;
#else
    public const bool AreDevToolsEnabled = false;
#endif
#endif
    // public const string DefaultHost = Constants.Hosts.LocalVoxt;
    public const string DefaultHost = IsDevApp ? Constants.Hosts.DevVoxt : Constants.Hosts.Voxt;
    public static readonly string Host;
    public static bool IsHostOverriden => !OrdinalIgnoreCaseEquals(Host, DefaultHost);
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly AppKind AppKind;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0C003D");

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
