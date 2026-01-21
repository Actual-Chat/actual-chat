using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine;

internal sealed class OpenSearchNames
{
    private const string IngestPipelineNameSuffix = "ingest-pipeline";
    private const string IndexNameSuffix = "index";
    public const string EntryIndexVersion = "v4";
    public const string UserIndexVersion = "v6";
    public const string GroupIndexVersion = "v5";
    public const string PlaceIndexVersion = "v3";

    public const string TestPrefix = "test";
    public const string ChatContent = "chat-content";
    public const string ChatContentCursor = "chat-content-cursor";
    public const string ChatCursor = "v2-chat-cursor";
    public string UniquePart { get; init; } = ""; // for testing purpose only
    public string Env { get; init; } = "";
    private string Prefix => ComposePrefix(Env, "sm", UniquePart); // sm == "Search Module"
    public string CommonIndexTemplateName => $"{Prefix}common";
    public string CommonIndexPattern => $"{Prefix}*";
    public string UserIndexName => $"{Prefix}users-{UserIndexVersion}";
    public string GroupIndexName => $"{Prefix}chats-{GroupIndexVersion}";
    public string PlaceIndexName => $"{Prefix}places-{PlaceIndexVersion}";
    public string EntryIndexName => $"{Prefix}entries-{EntryIndexVersion}";

    internal string GetFullName(string id, EmbeddingModelProps modelProps)
        => GetFull(Prefix, id, IndexNameSuffix, modelProps.UniqueKey);

    internal string GetFullIngestPipelineName(string id, EmbeddingModelProps modelProps)
        => GetFull(Prefix, id, IngestPipelineNameSuffix, modelProps.UniqueKey);

    private static string GetFull(params IEnumerable<string> parts)
        => string.Join("", parts.Select(ComposePrefix)).TrimEnd('-');

    private static string ComposePrefix(params IEnumerable<string> parts)
        => string.Join("", parts.Select(ComposePrefix));

    private static string ComposePrefix(string s)
    {
        s = s.Trim('-');
        return s.IsNullOrEmpty() ? "" : $"{s}-";
    }
}
