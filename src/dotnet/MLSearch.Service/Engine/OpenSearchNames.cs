using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine;

internal sealed class OpenSearchNames
{
    public const string MLPrefix = "ml";
    public const string IngestPipelineNameSuffix = "ingest-pipeline";
    public const string IndexNameSuffix = "index";
    public const string TemplateNameSuffix = "template";
    public const string EntryIndexVersion = "v3";
    public const string UserIndexVersion = "v5";
    public const string GroupIndexVersion = "v5";
    public const string PlaceIndexVersion = "v3";

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
    public string UserIndexName => $"{Prefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{Prefix}chats-{GroupIndexVersion}";
    public string PlaceIndexName => $"{Prefix}places-{PlaceIndexVersion}";
    public string EntryIndexName => $"{Prefix}entries-{EntryIndexVersion}";
    public string EntryCursorIndexName => $"{Prefix}entry-cursor-{EntryIndexVersion}";

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
}
