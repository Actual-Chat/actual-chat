namespace ActualChat.MLSearch.Indexing;

internal interface IDocumentMapper<in TSource, TDocument>
{
    ValueTask<TDocument> MapAsync(TSource source, CancellationToken cancellationToken = default);
}
