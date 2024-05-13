namespace ActualChat.MLSearch.Indexing;

internal interface ISink<in TDocument, in TDocumentId>
    where TDocument: IHasId<TDocumentId>
{
    Task ExecuteAsync(
        IReadOnlyCollection<TDocument>? updatedDocuments,
        IReadOnlyCollection<TDocumentId>? deletedDocuments,
        CancellationToken cancellationToken = default);
}
