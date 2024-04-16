using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatEntryLoader
{
    Task<IReadOnlyCollection<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class ChatEntryLoader(IChatsBackend chatsBackend): IChatEntryLoader
{
    public async Task<IReadOnlyCollection<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken)
    {
        return await chatsBackend.GetEntries(entryIds, true, cancellationToken).ConfigureAwait(false);
    }
}
