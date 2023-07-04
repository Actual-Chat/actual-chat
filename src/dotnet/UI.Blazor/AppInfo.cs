namespace ActualChat.UI.Blazor;

public static class AppInfo
{
    public static readonly string DisplayVersion =
        "v" + (typeof(AppInfo).Assembly.GetInformationalVersion() ?? "n/a").Replace('+', ' ');
}
