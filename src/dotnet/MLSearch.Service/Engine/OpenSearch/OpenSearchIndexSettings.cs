using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

public class OpenSearchIndexSettings(string indexName, OpenSearchClusterSettings settings)
{
    public Id IngestPipelineId { get; } = settings.IntoFullIngestPipelineName(indexName);
    public IndexName SearchIndexName { get; } = settings.IntoFullSearchIndexName(indexName);
    public IndexName CursorIndexName { get; } = settings.IntoFullCursorIndexName(indexName);
}
