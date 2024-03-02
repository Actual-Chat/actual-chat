using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Engine.OpenSearch.Setup;

namespace ActualChat.MLSearch.Engine.OpenSearch;

internal interface IIndexSettingsSource
{
    IndexSettings GetSettings<TDocument>();
}

internal class IndexSettingsSource(ClusterSetup clusterSetup): IIndexSettingsSource
{
    public IndexSettings GetSettings<TDocument>()
    {
        if (typeof(TDocument) == typeof(ChatSlice)) {
            return new IndexSettings(IndexNames.ChatSlice, clusterSetup.Result);
        }
        if (typeof(TDocument) == typeof(ChatEntriesIndexing.CursorState)) {
            return new IndexSettings(IndexNames.ChatSliceCursor, clusterSetup.Result);
        }

        throw new InvalidOperationException($"Document type '{typeof(TDocument).FullName}' is not configured.");
    }
}
