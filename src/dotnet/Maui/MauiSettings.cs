using ActualChat.Hosting;
using Microsoft.Maui.Graphics;

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
    // ReSharper disable once HeuristicUnreachableCode
    public const string ReverseDomain = IsDevApp ? "ai.voxt.dev" : "ai.voxt";
    // ReSharper disable once HeuristicUnreachableCode
    public const string AppScheme = IsDevApp ? Constants.AppSchemes.Dev : Constants.AppSchemes.Prod;
    // It's the URI host, not a path — the Android intent filter matches on DataHost.
    public const string AuthCallbackHost = Constants.AppSchemes.AuthCallbackHost;
    public const string AuthCallbackUrl = $"{AppScheme}://{AuthCallbackHost}";
    public const string DefaultHost =
        UseLocalhost
            ? Constants.Hosts.LocalVoxt
            : IsDevApp
                ? Constants.Hosts.DevVoxt
                : Constants.Hosts.Voxt;
    public static readonly string Host;
    public static bool IsHostOverridden => !string.Equals(Host, DefaultHost, StringComparison.OrdinalIgnoreCase);
    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;
    public static readonly AppKind AppKind;
    public static readonly string AppUserAgent;
    public static readonly Color SplashBackgroundColor = Color.FromArgb("#0C003D");

    static MauiSettings()
    {
        Host = MauiPreferences.HostOverride ?? DefaultHost;
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
        AppUserAgent = AppKind.ToUserAgent(ApiConstants.FullVersionString);
        MauiHostNameRemapper.Use();
    }

    // Nested types

    public static class Diagnostics
    {
        // Persists startup-phase marks + reports previous abnormal process exits (ANRs, native
        // crashes) via ApplicationExitInfo on the next launch. See MauiStartupBreadcrumbs.
        public const bool EnableStartupBreadcrumbs = true;
        // Warn-logs main-looper message dispatches that block the main thread for too long.
        // Android-only; see AndroidMainThreadMonitor.
        public const bool EnableMainThreadMonitor = true;
    }
}
