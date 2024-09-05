namespace ActualChat;

public static class Links
{
    public const string ChatEntryLidQueryParameterName = "n";

    public static readonly LocalUrl Home = default;
    public static readonly LocalUrl Docs = "/docs";
    public static readonly LocalUrl NotFound = "/404";
    public static readonly LocalUrl Chats = "/chat";

    public static LocalUrl Chat(ChatEntryId entryId)
        => entryId.IsNone
            ? "/chat"
            : $"/chat/{entryId.ChatId.Value}"  + ChatEntryQuery(entryId.LocalId);

    public static LocalUrl Chat(ChatId chatId, long? entryId = null)
        => entryId is { } vEntryId and > 0
            ? $"/chat/{chatId.Value}" + ChatEntryQuery(vEntryId)
            : $"/chat/{chatId.Value}";

    public static LocalUrl EmbeddedChat(ChatId chatId, long? entryId = null)
        => entryId is { } vEntryId and > 0
            ? $"/embedded/{chatId.Value}" + ChatEntryQuery(vEntryId)
            : $"/embedded/{chatId.Value}";

    public static LocalUrl User(UserId userId)
        => $"/u/{userId.Value.UrlEncode()}";

    public static LocalUrl Invite(string format, string inviteId)
        => string.Format(CultureInfo.InvariantCulture, format, inviteId.UrlEncode());

    public static LocalUrl CloseFlow(string flowName, bool mustClose = true, string? redirectUrl = null)
    {
        var url = $"/fusion/close?flow={flowName.UrlEncode()}";
        if (!mustClose)
            url += "&mustClose=0"; // "must close" is the default
        if (!redirectUrl.IsNullOrEmpty())
            url += $"&redirectUrl={redirectUrl.UrlEncode()}";
        return url;
    }

    private static string ChatEntryQuery(long entryLid)
        => $"?{ChatEntryLidQueryParameterName}={entryLid.Format()}";

    public static class Apps
    {
        public static readonly string Android = "https://play.google.com/store/apps/details?id=chat.actual.app";
        public static readonly string iOS = "https://apps.apple.com/us/app/actual-chat/id6450874551";
        public static readonly string Windows = "https://www.microsoft.com/store/apps/9N6RWRD9FMS2";

        public static class TestBuilds
        {
            public static readonly string iOS = "https://testflight.apple.com/join/5JP64q4v";
        }
    }
}
