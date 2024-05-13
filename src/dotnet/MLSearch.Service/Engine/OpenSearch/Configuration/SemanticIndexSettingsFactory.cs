
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal sealed class SemanticIndexSettingsFactory(IndexNames indexNames, IClusterSetup clusterSetup)
    : IOptionsFactory<SemanticIndexSettings>
{
    public SemanticIndexSettings Create(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var embeddingModelProps = clusterSetup.Result.EmbeddingModelProps;
        var indexName = indexNames.GetFullName(name, embeddingModelProps);
        var ingestPipelineName = indexNames.GetFullIngestPipelineName(name, embeddingModelProps);
        return new SemanticIndexSettings(indexName, embeddingModelProps.Id, ingestPipelineName);
    }
}
