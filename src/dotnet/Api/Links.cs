namespace ActualChat;

/// <summary>
/// Factory methods for building application URLs.
/// </summary>
public static class Links
{
    public const string ChatEntryLidQueryParameterName = "n";
    public const string Separator = "/";
    public const string AliasPrefix = "@";
    public const string ChatAliasPrefix = "/chat/@";
    public const string UserAliasPrefix = "/u/@";

    public static readonly LocalUrl Home = default;
    public static readonly LocalUrl Docs = "/docs";
    public static readonly LocalUrl Privacy = "/docs/privacy";
    public static readonly LocalUrl NotFound = "/404";
    public static readonly LocalUrl Chats = "/chat";
    public static readonly LocalUrl TestPageHome = "/test/blazor";


    public static LocalUrl Chat(ChatEntryId? ChatEntryId)
        => ChatEntryId == null
            ? "/chat"
            : $"/chat/{ChatEntryId.ChatId.Value}{TextEntryQuery(ChatEntryId.LocalId)}";

    public static LocalUrl Chat(ChatId chatId, long ChatEntryId = 0)
        => $"/chat/{chatId.Value}{TextEntryQuery(ChatEntryId)}";

    public static LocalUrl Chat(AliasInfo<ChatId> aliasInfo, long ChatEntryId = 0)
    {
        var chatId = aliasInfo.Id;
        if (chatId is PlaceChatId)
            throw new ArgumentOutOfRangeException(nameof(aliasInfo), "Place chat requires place alias info.");

        return aliasInfo.AliasId is { } aliasId
            ? $"/chat/@{aliasId.Value}{TextEntryQuery(ChatEntryId)}"
            : Chat(chatId, ChatEntryId);
    }

    public static LocalUrl Chat(AliasInfo<ChatId> aliasInfo, AliasInfo<PlaceId>? placeAliasInfo, long ChatEntryId = 0)
    {
        var chatId = aliasInfo.Id;
        if (chatId is not PlaceChatId placeChatId)
            return placeAliasInfo is null
                ? Chat(aliasInfo, ChatEntryId)
                : throw new ArgumentOutOfRangeException(nameof(placeAliasInfo),
                    "Chat doesn't belong to a place, but place alias info is provided.");

        ArgumentNullException.ThrowIfNull(placeAliasInfo,
            "Chat belongs to a place, but no place alias info is provided.");
        if (placeAliasInfo.Id != placeChatId.PlaceId)
            throw new ArgumentOutOfRangeException(nameof(placeAliasInfo),
                "Chat belongs to a place that differs from place alias info.");

        if (placeAliasInfo.AliasId is null) // Should we allow chat aliases for places w/o an alias?
            return Chat(chatId, ChatEntryId);

        var fullAlias = aliasInfo.AliasId is not { } aliasId
            ? string.Concat(placeAliasInfo.AliasId.Value, Separator, placeChatId.LocalChatId)
            : string.Concat(placeAliasInfo.AliasId.Value, Separator, AliasPrefix, aliasId.Value);
        return $"/chat/@{fullAlias}" + TextEntryQuery(ChatEntryId);
    }

    public static LocalUrl EmbeddedChat(ChatId chatId, long textEntryLid = 0)
        => textEntryLid > 0
            ? $"/embedded/{chatId.Value}" + TextEntryQuery(textEntryLid)
            : $"/embedded/{chatId.Value}";

    public static LocalUrl PlaceInfo(PlaceId placeId)
        => $"/place/{placeId.Value}";

    public static LocalUrl User(UserId userId)
        => $"/u/{userId.Value.UrlEncode()}";

    public static LocalUrl User(AliasInfo<UserId> aliasInfo)
        => aliasInfo.AliasId is { } userAliasId
            ? $"/u/@{userAliasId.Value.UrlEncode()}"
            : User(aliasInfo.Id);

    public static LocalUrl Invite(string format, string inviteId)
        => string.Format(format, inviteId.UrlEncode());

    public static LocalUrl CloseFlow(
        string flowName,
        bool mustClose = true,
        string? redirectUrl = null)
    {
        var url = $"/fusion/close?flow={flowName.UrlEncode()}";
        if (!mustClose)
            url += "&mustClose=0"; // "must close" is the default
        if (!redirectUrl.IsNullOrEmpty())
            url += $"&redirectUrl={redirectUrl.UrlEncode()}";
        return url;
    }

    private static string TextEntryQuery(long textEntryLid)
        => textEntryLid == 0
            ? ""
            : $"?{ChatEntryLidQueryParameterName}={textEntryLid.Format()}";

    public static class Apps
    {
        public static readonly string Android = "https://play.google.com/store/apps/details?id=chat.actual.app";
        public static readonly string iOS = "https://apps.apple.com/us/app/actual-chat/id6450874551";
        public static readonly string Windows = "https://www.microsoft.com/store/apps/9N6RWRD9FMS2";
        // The two custom-scheme links open the store app on the product page; their https
        // counterparts would open a browser instead, which is wrong for an "update now" tap.
        public static readonly string MacOSApp = "macappstore://apps.apple.com/app/id6450874551";
        public static readonly string WindowsApp = "ms-windows-store://pdp/?productid=9N6RWRD9FMS2";

        public static string? Store(AppKind appKind)
            // null means "this kind has no store", i.e. the web app
            => appKind switch {
                AppKind.Android => Android,
                AppKind.Ios => iOS,
                AppKind.MacOS => MacOSApp,
                AppKind.Windows => WindowsApp,
                _ => null,
            };

        public static class TestBuilds
        {
            public static readonly string iOS = "https://testflight.apple.com/join/5JP64q4v";
        }
    }
}
