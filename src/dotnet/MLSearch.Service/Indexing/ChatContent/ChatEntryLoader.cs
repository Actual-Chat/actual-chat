using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatEntryLoader
{
    Task<IReadOnlyList<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken);
}

internal class ChatEntryLoader(IChatsBackend chatsBackend): IChatEntryLoader
{
    public async Task<IReadOnlyList<ChatEntry>> LoadByIdsAsync(
        IEnumerable<ChatEntryId> entryIds,
        CancellationToken cancellationToken) => await chatsBackend.GetEntries(entryIds, true, cancellationToken).ConfigureAwait(false);
}
