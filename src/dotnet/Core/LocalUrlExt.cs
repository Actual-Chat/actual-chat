using System.Text.RegularExpressions;

namespace ActualChat;

public static partial class LocalUrlExt
{
    [GeneratedRegex(@"^\/chat\/(?<chatId>[a-z0-9-]+)(?<parameters>\?[^#]*)?#(?<hash>.*)?")]
    private static partial Regex IsChatRegexFactory();

    private static readonly Regex IsChatRegex = IsChatRegexFactory();

    public static bool IsHome(this LocalUrl url)
        => url == Links.Home || url.Value.OrdinalStartsWith("/?");

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

    public static bool IsChat(this LocalUrl url, out ChatId chatId)
        => url.IsChat(out chatId, out _, out _);
    public static bool IsChat(this LocalUrl url, out ChatId chatId, out long entryLid)
    {
        entryLid = 0;
        if (!url.IsChat(out chatId, out _, out var hash))
            return false;

        _ = NumberExt.TryParsePositiveLong(hash, out entryLid);
        return true;
    }

    public static bool IsChat(this LocalUrl url, out ChatId chatId, out string hash)
        => url.IsChat(out chatId, out _, out hash);
    public static bool IsChat(this LocalUrl url, out ChatId chatId, out string parameters, out string hash)
    {
        chatId = default;
        parameters = "";
        hash = "";
        var match = IsChatRegex.Match(url);
        if (!match.Success)
            return false;

        chatId = ChatId.ParseOrNone(match.Groups["chatId"].Value);
        parameters = match.Groups["parameters"].Value;
        hash = match.Groups["hash"].Value;
        return !chatId.IsNone;
    }

    public static bool IsUser(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/u/");

    public static bool IsSettings(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/settings");

    public static bool IsPrivateChatInvite(this LocalUrl url)
        => url.Value.OrdinalStartsWith("/join/");
}
