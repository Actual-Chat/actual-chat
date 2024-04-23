using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class IndexSettings(string indexId, IndexNames indexNames, ClusterSettings settings)
{
    public string ModelId => settings.ModelId;

    public string IngestPipelineId { get; } = indexNames.GetFullIngestPipelineName(indexId, settings);
    public IndexName IndexName { get; } = indexNames.GetFullName(indexId, settings);
}
