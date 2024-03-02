using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

public class IndexSettings(string indexName, ClusterSettings settings)
{
    public string ModelId => settings.ModelId;

    public string IngestPipelineId { get; } = settings.IntoFullIngestPipelineName(indexName);
    public IndexName SearchIndexName { get; } = settings.IntoFullSearchIndexName(indexName);
    public IndexName CursorIndexName { get; } = settings.IntoFullCursorIndexName(indexName);
}
