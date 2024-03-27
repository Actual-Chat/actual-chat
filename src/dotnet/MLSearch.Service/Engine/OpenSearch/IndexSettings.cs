using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

public class IndexSettings(string indexName, ClusterSettings settings)
{
    public string ModelId => settings.ModelId;

    public string IngestPipelineId { get; } = settings.IntoFullIngestPipelineName(indexName);
    public IndexName IndexName { get; } = settings.IntoFullIndexName(indexName);
}
