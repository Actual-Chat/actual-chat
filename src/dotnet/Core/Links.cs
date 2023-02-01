namespace ActualChat;

public static class Links
{
    public static LocalUrl Home { get; } = default;
    public static LocalUrl Docs { get; } = "/docs";

    public static LocalUrl Chat(ChatEntryId entryId)
        => entryId.IsNone
            ? "/chat"
            : $"/chat/{entryId.ChatId.Value}#{entryId.LocalId.Format()}";

    public static LocalUrl Chat(ChatId chatId, long? entryId = null)
        => entryId is { } vEntryId
            ? $"/chat/{chatId.Value}#{vEntryId.Format()}"
            : $"/chat/{chatId.Value}";

    public static LocalUrl User(UserId userId)
        => $"/u/{userId.Value.UrlEncode()}";

    public static LocalUrl Settings()
        => "/settings";

    public static LocalUrl Invite(string format, string inviteId)
        => string.Format(CultureInfo.InvariantCulture, format, inviteId.UrlEncode());

    public static LocalUrl SignOut(string redirectUrl = "")
        =>  redirectUrl.IsNullOrEmpty()
            ? "sign-out"
            : $"sign-out/{redirectUrl.UrlEncode()}";
}
