using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatEntryLoader
{
    Task<IReadOnlyCollection<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class ChatEntryLoader: IChatEntryLoader
{
    public Task<IReadOnlyCollection<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
