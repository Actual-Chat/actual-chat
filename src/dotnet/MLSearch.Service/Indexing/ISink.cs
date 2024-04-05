namespace ActualChat.MLSearch.Indexing;

internal interface ISink<in TUpdated, in TDeleted>
{
    Task ExecuteAsync(
        IEnumerable<TUpdated>? updatedDocuments,
        IEnumerable<TDeleted>? deletedDocuments,
        CancellationToken cancellationToken);
}
