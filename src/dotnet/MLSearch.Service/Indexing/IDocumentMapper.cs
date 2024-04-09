namespace ActualChat.MLSearch.Indexing;

internal interface IDocumentMapper<in TSource, in TSourceId, out TDocument>
{
    TDocument Map(TSource source);
    string MapId(TSourceId source);
}
