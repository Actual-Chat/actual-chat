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
}
