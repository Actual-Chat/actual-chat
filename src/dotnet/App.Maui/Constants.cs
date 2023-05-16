namespace ActualChat.App.Maui;

public static class MauiConstants
{
#if ISDEVMAUI
    public const string Host = "dev.actual.chat";
#else
    public const string Host = "actual.chat";
#endif
    public const string AppSettingsFileName = "appsettings.json";
}
