using Elastic.Clients.Elasticsearch;

namespace ActualChat.Search;

public sealed class ElasticNames
{
    public const string EntryIndexVersion = "v2";
    public const string UserIndexVersion = "v2";
    public const string ChatIndexVersion = "v2";

    public string IndexPrefix { get; init; } = ""; // for testing purpose only
    private string EntryIndexNamePrefix => $"{IndexPrefix}entries-{EntryIndexVersion}";
    public string EntryIndexTemplateName => EntryIndexNamePrefix;
    public string EntryIndexPattern => $"{EntryIndexNamePrefix}-*";
    public string PublicUserIndexName => $"{IndexPrefix}users-{UserIndexVersion}";
    private string PublicChatIndexName => $"{IndexPrefix}public-chats-{ChatIndexVersion}";
    private string PrivateChatIndexName => $"{IndexPrefix}private-chats-{ChatIndexVersion}";

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
