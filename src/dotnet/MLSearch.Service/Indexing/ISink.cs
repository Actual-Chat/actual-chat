namespace ActualChat.MLSearch.Indexing;

internal interface ISink<TDocument, TDocumentId>
    where TDocument: IHasId<TDocumentId>
{
    Task ExecuteAsync(
        IEnumerable<TDocument>? updatedDocuments,
        IEnumerable<TDocumentId>? deletedDocuments,
        CancellationToken cancellationToken);
}
