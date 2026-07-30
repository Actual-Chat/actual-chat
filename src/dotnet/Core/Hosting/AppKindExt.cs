namespace ActualChat.Hosting;

public static class AppKindExt
{
    public static bool IsMaui(this AppKind appKind)
        => appKind is AppKind.Ios or AppKind.MacOS or AppKind.Android or AppKind.Windows;

    public static bool IsMobile(this AppKind appKind)
        => appKind is AppKind.Ios or AppKind.Android;

    public static bool IsApple(this AppKind appKind)
        => appKind is AppKind.Ios or AppKind.MacOS;

    public static bool HasJit(this AppKind appKind)
        => appKind is AppKind.Android or AppKind.Windows;

    public static string ToUserAgent(this AppKind appKind, string version)
        => $"{appKind:G}App/{version}";

    public static bool TryParseUserAgent(string? userAgent, out AppKind appKind)
    {
        // Recognizes only the user agents produced by ToUserAgent - i.e. app ones, not browser ones
        appKind = AppKind.Unknown;
        if (userAgent.IsNullOrEmpty())
            return false;

        var slashIndex = userAgent.IndexOf('/');
        if (slashIndex <= 0)
            return false;

        var prefix = userAgent[..slashIndex];
        if (!prefix.EndsWith("App"))
            return false;

        return Enum.TryParse(prefix[..^3], out appKind) && Enum.IsDefined(appKind);
    }
}
