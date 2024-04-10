using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDocumentLoader
{
    Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class DocumentLoader: IDocumentLoader
{
    public Task<IReadOnlyCollection<ChatSlice>> LoadTailAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyCollection<ChatSlice>> LoadByEntryIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
