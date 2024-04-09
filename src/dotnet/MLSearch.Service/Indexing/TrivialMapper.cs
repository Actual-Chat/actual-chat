
namespace ActualChat.MLSearch.Indexing;

internal class TrivialMapper<TDocument> : IDocumentMapper<TDocument, string, TDocument>
{
    public TDocument Map(TDocument source) => source;
    public string MapId(string sourceId) => sourceId;
}
