namespace ActualChat.MLSearch.Engine.Indexing;

internal interface ISink<in TSource>
{
    Task ExecuteAsync(
        IEnumerable<TSource>? updatedDocuments,
        IEnumerable<TSource>? deletedDocuments,
        CancellationToken cancellationToken);
}
