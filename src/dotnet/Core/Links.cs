namespace ActualChat;

public static class Links
{
    public static string ChatPage(string chatId, long? entryId = null)
        => entryId.HasValue
            ? $"/chat/{chatId}#{entryId}"
            : $"/chat/{chatId}";

    public static string UserPage(string userId)
        => $"/u/{userId.UrlEncode()}";

    public static string SettingsPage()
        => "/settings";

    public static string Invite(string format, string inviteId)
        => string.Format(CultureInfo.InvariantCulture, format, inviteId.UrlEncode());

    public static bool Equals(string link1, string link2)
        => OrdinalEquals(Normalize(link1), Normalize(link2));

    public static string Normalize(string link)
    {
        if (!link.OrdinalEndsWith("/"))
            link += "/";
        if (!link.OrdinalStartsWith("/"))
            link = "/" + link;
        return link;
    }
}
