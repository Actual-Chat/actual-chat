namespace ActualChat.MLSearch.Indexing;

internal interface ISink<in TDocument, in TDocumentId>
    where TDocument: IHasId<TDocumentId>
{
    Task ExecuteAsync(
        IEnumerable<TDocument>? updatedDocuments,
        IEnumerable<TDocumentId>? deletedDocuments,
        CancellationToken cancellationToken = default);
}
