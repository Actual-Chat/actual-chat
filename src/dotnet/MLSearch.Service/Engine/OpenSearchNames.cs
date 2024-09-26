using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine;

internal sealed class OpenSearchNames
{
    public string UniquePart { get; init; } = ""; // for testing purpose only
    private const string MLModulePrefix = "ml";
    private const string IngestPipelineNameSuffix = "ingest-pipeline";
    private const string IndexNameSuffix = "index";
    private const string TemplateNameSuffix = "template";
    public const string EntryIndexVersion = "v3";
    public const string UserIndexVersion = "v5";
    public const string GroupIndexVersion = "v5";
    public const string PlaceIndexVersion = "v3";
    public const string TestPrefix = "test";
    public const string ChatContent = "chat-content";
    public const string ChatContentCursor = "chat-content-cursor";
    public const string ChatListCursor = "chat-cursor";
    public static string MLIndexPattern => $"{MLModulePrefix}-*";
    public static string MLTestIndexPattern => $"{MLModulePrefix}-{TestPrefix}-*";
    public static string MLTemplateName => $"{MLModulePrefix}-{TemplateNameSuffix}";
    private string ModuleUniquePrefix => string.IsNullOrEmpty(UniquePart) ? MLModulePrefix : $"{MLModulePrefix}-{UniquePart}";
    private string SearchModulePrefix => string.IsNullOrEmpty(UniquePart) ? "sm-" : $"sm-{UniquePart}-"; // sm == "Search Module"
    public string CommonIndexTemplateName => $"{SearchModulePrefix}common";
    public string CommonIndexPattern => $"{SearchModulePrefix}*";
    public string UserIndexName => $"{SearchModulePrefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{SearchModulePrefix}chats-{GroupIndexVersion}";
    public string PlaceIndexName => $"{SearchModulePrefix}places-{PlaceIndexVersion}";
    public string EntryIndexName => $"{SearchModulePrefix}entries-{EntryIndexVersion}";
    public string EntryCursorIndexName => $"{SearchModulePrefix}entry-cursor-{EntryIndexVersion}";

    internal string GetIndexName(string indexName, EmbeddingModelProps modelProps)
        => string.Join('-',
            ModuleUniquePrefix,
            indexName,
            IndexNameSuffix,
            modelProps.UniqueKey);

    internal string GetIngestPipelineName(string indexName, EmbeddingModelProps modelProps)
        => string.Join('-',
            ModuleUniquePrefix,
            indexName,
            IngestPipelineNameSuffix,
            modelProps.UniqueKey);
}
