namespace ActualChat;

public static class LocalUrlExt
{
    public static bool IsHome(this LocalUrl url)
        => url == Links.Home;

    public static bool IsDocs(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/docs");

    public static bool IsChatOrChatRoot(this LocalUrl url)
        => url.IsChatRoot() || url.IsChat();

    public static bool IsChatRoot(this LocalUrl url)
        => OrdinalEquals(url.Value, "/chat") || OrdinalEquals(url.Value, "/chat/");

    public static bool IsChat(this LocalUrl url)
        => url.Value.Length > 6 && url.Value.OrdinalStartsWith("/chat/");

    public static bool IsUserRoot(this LocalUrl url)
        => OrdinalEquals(url.Value, "/u");

    public static bool IsUser(this LocalUrl url)
        => url.IsUserRoot() || url.Value.OrdinalStartsWith("/u");

    public static bool IsSettings(this LocalUrl url)
        => OrdinalEquals(url.Value, "/settings");
}
