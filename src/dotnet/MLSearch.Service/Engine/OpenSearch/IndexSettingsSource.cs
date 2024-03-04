using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine.OpenSearch;

// TODO: KILL THIS. 
internal interface IIndexSettingsSource
{
    IndexSettings GetSettings<TDocument>();
}

internal class IndexSettingsSource(ClusterSetup clusterSetup): IIndexSettingsSource
{
    public IndexSettings GetSettings<TDocument>()
    {
        if (typeof(TDocument) == typeof(ChatSlice)) {
            return new IndexSettings("chat-slice", clusterSetup.Result);
        }

        throw new InvalidOperationException($"Document type '{typeof(TDocument).FullName}' is not configured.");
    }
}
