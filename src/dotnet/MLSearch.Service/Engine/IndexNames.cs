using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine;

internal sealed class IndexNames
{
    public const string MLPrefix = "ml";
    public const string IngestPipelineNameSuffix = "ingest-pipeline";
    public const string IndexNameSuffix = "index";
    public const string TemplateNameSuffix = "template";
    public const string EntryIndexVersion = "v2"; // TODO: remove
    public const string UserIndexVersion = "v5";
    public const string ChatIndexVersion = "v5";
    public const string PlaceIndexVersion = "v2";

    public const string TestPrefix = "test";
    public const string ChatContent = "chat-content";
    public const string ChatContentCursor = "chat-content-cursor";
    public const string ChatCursor = "chat-cursor";
    public string UniquePart { get; init; } = ""; // for testing purpose only
    public static string MLIndexPattern => $"{MLPrefix}-*";
    public static string MLTestIndexPattern => $"{MLPrefix}-{TestPrefix}-*";
    public static string MLTemplateName => $"{MLPrefix}-{TemplateNameSuffix}";
    private string MLFullPrefix => string.IsNullOrEmpty(UniquePart) ? MLPrefix : $"{MLPrefix}-{UniquePart}";
    private string Prefix => string.IsNullOrEmpty(UniquePart) ? "sm-" : $"sm-{UniquePart}-"; // sm == "Search Module"
    public string CommonIndexTemplateName => $"{Prefix}common";
    public string CommonIndexPattern => $"{Prefix}*";
    private string EntryIndexNamePrefix => $"{Prefix}entries-{EntryIndexVersion}"; // TODO: remove
    public string EntryIndexTemplateName => EntryIndexNamePrefix.Trim('-'); // TODO: remove
    public string EntryIndexPattern => $"{EntryIndexNamePrefix}*"; // TODO: remove
    public string UserIndexName => $"{Prefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{Prefix}chats-{ChatIndexVersion}";
    public string PlaceIndexName => $"{Prefix}places-{PlaceIndexVersion}";

    internal string GetFullName(string id, EmbeddingModelProps modelProps)
        => string.Join('-',
            MLFullPrefix,
            id,
            IndexNameSuffix,
            modelProps.UniqueKey);

    internal string GetFullIngestPipelineName(string id, EmbeddingModelProps modelProps)
        => string.Join('-',
            MLFullPrefix,
            id,
            IngestPipelineNameSuffix,
            modelProps.UniqueKey);
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
