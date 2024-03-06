using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine.OpenSearch;

// TODO: KILL THIS.
internal interface IIndexSettingsSource
{
    IndexSettings GetSettings(string indexName);
}

internal class IndexSettingsSource(ClusterSetup clusterSetup): IIndexSettingsSource
{
    public IndexSettings GetSettings(string indexName) => new (indexName, clusterSetup.Result);
}
