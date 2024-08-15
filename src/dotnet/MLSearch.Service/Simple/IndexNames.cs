using OpenSearch.Client;

namespace ActualChat.Search;

public sealed class IndexNames
{
    public const string EntryIndexVersion = "v2"; // TODO: remove
    public const string UserIndexVersion = "v5";
    public const string ChatIndexVersion = "v5";
    public const string PlaceIndexVersion = "v2";

    public string IndexPrefix { get; init; } = ""; // for testing purpose only
    private string CommonIndexNamePrefix => string.IsNullOrEmpty(IndexPrefix) ? "sm-" : $"sm-{IndexPrefix}-"; // sm == "Search Module"
    public string CommonIndexTemplateName => $"{CommonIndexNamePrefix}common";
    public string CommonIndexPattern => $"{CommonIndexNamePrefix}*";
    private string EntryIndexNamePrefix => $"{CommonIndexNamePrefix}entries-{EntryIndexVersion}"; // TODO: remove
    public string EntryIndexTemplateName => EntryIndexNamePrefix.Trim('-'); // TODO: remove
    public string EntryIndexPattern => $"{EntryIndexNamePrefix}*"; // TODO: remove
    public string UserIndexName => $"{CommonIndexNamePrefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{CommonIndexNamePrefix}chats-{ChatIndexVersion}";
    public string PlaceIndexName => $"{CommonIndexNamePrefix}places-{PlaceIndexVersion}";

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

    // TODO: remove
    public IEnumerable<IndexName> GetPeerChatSearchIndexNamePatterns(UserId userId)
    {
        yield return $"{EntryIndexNamePrefix}-p-{userId.Value.ToLowerInvariant()}-*";
        yield return $"{EntryIndexNamePrefix}-p-*-{userId.Value.ToLowerInvariant()}";
    }
}
