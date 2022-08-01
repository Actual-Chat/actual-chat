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

    public static string InviteLink(string linkFormat, string inviteId)
        => string.Format(CultureInfo.InvariantCulture, linkFormat, inviteId.UrlEncode());
}
