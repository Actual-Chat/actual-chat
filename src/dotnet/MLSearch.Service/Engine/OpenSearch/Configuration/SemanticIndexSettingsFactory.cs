
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal sealed class SemanticIndexSettingsFactory(OpenSearchNames openSearchNames, IClusterSetup clusterSetup)
    : IOptionsFactory<SemanticIndexSettings>
{
    public SemanticIndexSettings Create(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var embeddingModelProps = clusterSetup.Result.EmbeddingModelProps;
        var indexName = openSearchNames.GetIndexName(name, embeddingModelProps);
        var ingestPipelineName = openSearchNames.GetIngestPipelineName(name, embeddingModelProps);
        return new SemanticIndexSettings(indexName, embeddingModelProps.Id, ingestPipelineName);
    }
}
