
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch.Configuration;

internal sealed class SemanticIndexSettingsFactory(IndexNames indexNames, IClusterSetup clusterSetup)
    : IOptionsFactory<SemanticIndexSettings>
{
    public SemanticIndexSettings Create(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var clusterSettings = clusterSetup.Result;
        var indexName = indexNames.GetFullName(name, clusterSettings);
        var ingestPipelineName = indexNames.GetFullIngestPipelineName(name, clusterSettings);
        return new SemanticIndexSettings(indexName, clusterSettings.ModelId, ingestPipelineName);
    }
}
