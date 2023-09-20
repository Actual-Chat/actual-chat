namespace ActualChat;

public static class Links
{
    public static LocalUrl Home { get; } = default;
    public static LocalUrl Docs { get; } = "/docs";
    public static LocalUrl NotFound { get; } = "/404";
    public static LocalUrl Chats { get; } = "/chat";

    public static LocalUrl Chat(ChatEntryId entryId)
        => entryId.IsNone
            ? "/chat"
            : $"/chat/{entryId.ChatId.Value}#{entryId.LocalId.Format()}";

    public static LocalUrl Chat(ChatId chatId, long? entryId = null)
        => entryId is { } vEntryId and > 0
            ? $"/chat/{chatId.Value}#{vEntryId.Format()}"
            : $"/chat/{chatId.Value}";

    public static LocalUrl EmbeddedChat(ChatId chatId, long? entryId = null)
        => entryId is { } vEntryId and > 0
            ? $"/embedded/{chatId.Value}#{vEntryId.Format()}"
            : $"/embedded/{chatId.Value}";

    public static LocalUrl User(UserId userId)
        => $"/u/{userId.Value.UrlEncode()}";

    public static LocalUrl Invite(string format, string inviteId)
        => string.Format(CultureInfo.InvariantCulture, format, inviteId.UrlEncode());

    public static LocalUrl SignOut(LocalUrl redirectUrl = default)
        => $"signOut?returnUrl={redirectUrl.Value.UrlEncode()}";

    public static LocalUrl AutoClose(string flowOrActionName)
        => "/fusion/close?flow=" + flowOrActionName.UrlEncode();

    public static class Apps
    {
        public static readonly string Android = "https://play.google.com/store/apps/details?id=chat.actual.app";
        public static readonly string iOS = "https://testflight.apple.com/join/5JP64q4v";
        public static readonly string Windows = "https://www.microsoft.com/store/apps/9N6RWRD9FMS2";
    }

    public static class InviteLinkFormat
    {
        public const string JoinChat = "join/{0}";
        public const string InviteUser = "user/invite/{0}";
    }
}
