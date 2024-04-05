
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal interface IIndexSettingsSource
{
    IndexSettings GetSettings(string indexName);
}

internal sealed class IndexSettingsSource(ClusterSetup clusterSetup): IIndexSettingsSource
{
    public IndexSettings GetSettings(string indexName) => new (indexName, clusterSetup.Result);
}
