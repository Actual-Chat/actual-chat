using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine;

internal sealed class IndexNames
{
    private const string NamePrefix = "ml";
    private const string IngestPipelineNameSuffix = "ingest-pipeline";
    private const string IndexNameSuffix = "index";
    private const string TemplateNameSuffix = "template";

    public const string TestPrefix = "test";
    public const string ChatSlice = "chat-slice";
    public const string ChatSliceCursor = "chat-slice-ingest-cursor";
    public const string ChatCursor = "chat-cursor";
    public string IndexPrefix { get; init; } = ""; // for testing purpose only
    public static string MLIndexPattern => $"{NamePrefix}-*";
    public static string MLTestIndexPattern => $"{NamePrefix}-{TestPrefix}-*";
    public static string MLTemplateName => $"{NamePrefix}-{TemplateNameSuffix}";

    internal string GetFullName(string id, ClusterSettings settings)
        => string.Join('-',
            NamePrefix,
            IndexPrefix,
            id,
            IndexNameSuffix,
            settings.UniqueKey);

    internal string GetFullIngestPipelineName(string id, ClusterSettings settings)
        => string.Join('-',
            NamePrefix,
            IndexPrefix,
            id,
            IngestPipelineNameSuffix,
            settings.UniqueKey);
}
