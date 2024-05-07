
using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal sealed class IndexSettingsFactory(IndexNames indexNames, ClusterSetup clusterSetup) : IOptionsFactory<IndexSettings>
{
    public IndexSettings Create(string name)
    {
        var clusterSettings = clusterSetup.Result;
        var indexName = indexNames.GetFullName(name, clusterSettings);
        var ingestPipelineName = indexNames.GetFullIngestPipelineName(name, clusterSettings);
        return new IndexSettings(indexName, clusterSettings.ModelId, ingestPipelineName);
    }
}
