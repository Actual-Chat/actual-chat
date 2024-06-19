using OpenSearch.Client;

namespace ActualChat.Search;

public sealed class IndexNames
{
    public const string EntryIndexVersion = "v2";
    public const string UserIndexVersion = "v3";
    public const string ChatIndexVersion = "v2";

    public string IndexPrefix { get; init; } = ""; // for testing purpose only
    private string CommonIndexNamePrefix => string.IsNullOrEmpty(IndexPrefix) ? "sm-" : $"sm-{IndexPrefix}-"; // sm == "Search Module"
    public string CommonIndexTemplateName => $"{CommonIndexNamePrefix}common";
    public string CommonIndexPattern => $"{CommonIndexNamePrefix}*";
    private string EntryIndexNamePrefix => $"{CommonIndexNamePrefix}entries-{EntryIndexVersion}";
    public string EntryIndexTemplateName => EntryIndexNamePrefix.Trim('-');
    public string EntryIndexPattern => $"{EntryIndexNamePrefix}*";
    public string PublicUserIndexName => $"{CommonIndexNamePrefix}users-{UserIndexVersion}";
    private string PublicChatIndexName => $"{CommonIndexNamePrefix}public-chats-{ChatIndexVersion}";
    private string PrivateChatIndexName => $"{CommonIndexNamePrefix}private-chats-{ChatIndexVersion}";

    public IndexName GetIndexName(Chat.Chat chat)
        => GetIndexName(chat.Id, chat.IsPublicPlaceChat());

    public IndexName GetIndexName(ChatId chatId, bool isPublicPlaceChat)
        => isPublicPlaceChat || chatId.IsPlaceRootChat
            ? GetIndexName(chatId.PlaceId)
            : GetIndexName(chatId.Value);

    public IndexName GetIndexName(PlaceId placeId)
        => GetIndexName(placeId.Value);

    private string GetIndexName(string sid)
        => $"{EntryIndexNamePrefix}-{sid.ToLowerInvariant()}";

    public IEnumerable<IndexName> GetPeerChatSearchIndexNamePatterns(UserId userId)
    {
        yield return $"{EntryIndexNamePrefix}-p-{userId.Value.ToLowerInvariant()}-*";
        yield return $"{EntryIndexNamePrefix}-p-*-{userId.Value.ToLowerInvariant()}";
    }

    public IndexName GetChatContactIndexName(bool isPublic)
        => isPublic ? PublicChatIndexName : PrivateChatIndexName;
}
