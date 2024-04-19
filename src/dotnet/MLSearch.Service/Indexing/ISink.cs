namespace ActualChat.MLSearch.Indexing;

internal interface ISink<TDocument>
{
    Task ExecuteAsync(
        IEnumerable<TDocument>? updatedDocuments,
        IEnumerable<string>? deletedDocuments,
        CancellationToken cancellationToken);
}
