namespace ActualChat;

public static class LocalUrlExt
{
    public static bool IsHome(this LocalUrl url)
        => url == Links.Home;

    public static bool IsDocsOrDocsRoot(this LocalUrl url)
        => url.IsDocs() || url.IsDocsRoot();
    public static bool IsDocsRoot(this LocalUrl url)
        => OrdinalEquals(url.Value, "/docs");
    public static bool IsDocs(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/docs/");

    public static bool IsChatOrChatRoot(this LocalUrl url)
        => url.IsChat() || url.IsChatRoot();
    public static bool IsChatRoot(this LocalUrl url)
        => OrdinalEquals(url.Value, "/chat");
    public static bool IsChat(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/chat/");

    public static bool IsUser(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/u/");

    public static bool IsSettings(this LocalUrl url)
        => OrdinalEquals(url.Value, "/settings");
}
