namespace ActualChat.App.Maui;

public static class MauiSettings
{
    public const string LocalHost = "0.0.0.0";
#if IS_DEV_MAUI
    public const string Host = "dev.actual.chat";
#else
    public const string Host = "actual.chat";
#endif
    public const string AppSettingsFileName = "appsettings.json";

    public static readonly Uri BaseUri;
    public static readonly string BaseUrl;

    static MauiSettings()
    {
        BaseUrl = "https://" + Host + "/";
        BaseUri = BaseUrl.ToUri();
    }

    // Nested types

    public static class SignIn
    {
        public static bool UseWebView { get; } = true;
    }
}
