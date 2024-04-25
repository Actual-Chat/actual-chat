
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal interface IIndexSettingsSource
{
    IndexSettings GetSettings(string indexName);
}

internal sealed class IndexSettingsSource(IndexNames indexNames, ClusterSetup clusterSetup): IIndexSettingsSource
{
    public IndexSettings GetSettings(string indexId) => new (indexId, indexNames, clusterSetup.Result);
}
