using ActualChat.Hosting;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public static class MauiSettings
{
    public static readonly string LocalHost = "0.0.0.1";
    // NOTE: UseLocalhost is used only for development purposes. Do not commit it to the repo.
    public const bool UseLocalhost = false;
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
    public const string AppScheme = IsDevApp ? "voxt-dev" : "voxt";
    public const string DefaultHost =
        UseLocalhost
            ? Constants.Hosts.LocalVoxt :
            IsDevApp
                ? Constants.Hosts.DevVoxt
                : Constants.Hosts.Voxt;
    public static readonly string Host;
    public static bool IsHostOverridden => !OrdinalIgnoreCaseEquals(Host, DefaultHost);
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly AppKind AppKind;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0C003D");

    private const string HostOverridePreferenceKey = "app_server_instance_override";

    public static string? HostOverride {
        get => (field ??= Preferences.Default.Get(HostOverridePreferenceKey, "")).NullIfEmpty();
        set {
            field = value ?? "";
            if (value.IsNullOrEmpty())
                Preferences.Default.Remove(HostOverridePreferenceKey);
            else
                Preferences.Default.Set(HostOverridePreferenceKey, value);
        }
    }

    static MauiSettings()
    {
        Host = HostOverride ?? DefaultHost;
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
        MauiHostNameRemapper.Use();
    }

    // Nested types

    public static class WebAuth
    {
        public static readonly bool UseSystemBrowser = true;
    }
}
