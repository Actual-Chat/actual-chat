using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Indexing;

internal interface IDocumentMapper<in TSource, out TDocument>
{
    TDocument Map(TSource source);
    Id MapId(TSource source);
}
