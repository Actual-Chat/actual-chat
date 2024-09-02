
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
        var indexName = openSearchNames.GetFullName(name, embeddingModelProps);
        var ingestPipelineName = openSearchNames.GetFullIngestPipelineName(name, embeddingModelProps);
        return new SemanticIndexSettings(indexName, embeddingModelProps.Id, ingestPipelineName);
    }
}
