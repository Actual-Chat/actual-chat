using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentUpdateLoader
{
    IAsyncEnumerable<ChatEntry> LoadChatUpdatesAsync(ChatId targetId, ChatContentCursor cursor, CancellationToken cancellationToken);
}

internal class ChatContentUpdateLoader(
    int batchSize,
    IChatsBackend chats
) : IChatContentUpdateLoader
{
    public async IAsyncEnumerable<ChatEntry> LoadChatUpdatesAsync(
        ChatId targetId, ChatContentCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bool continueProcessing;
        var (lastEntryVersion, lastEntryLocalId) = (cursor.LastEntryVersion, cursor.LastEntryLocalId);
        do {
            // We must read all updated entries with LocalId <= lastEntryLocalId
            // before reading next batch. Otherwise, we risk to lose some updates.
            bool continueReadUpdates;
            do {
                var updatedEntries = await chats
                    .ListChangedEntries(targetId, lastEntryLocalId, lastEntryVersion, batchSize, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var entry in updatedEntries) {
                    lastEntryVersion = Math.Max(lastEntryVersion, entry.Version);
                    yield return entry;
                }
                continueReadUpdates = updatedEntries.Count == batchSize;
            }
            while (continueReadUpdates);

            // Now read next batch of entries in chat
            var createdEntries = await chats
                .ListNewEntries(targetId, lastEntryLocalId, batchSize, cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in createdEntries) {
                lastEntryVersion = Math.Max(lastEntryVersion, entry.Version);
                lastEntryLocalId = Math.Max(lastEntryLocalId, entry.LocalId);
                yield return entry;
            }
            continueProcessing = createdEntries.Count == batchSize;
        }
        while (continueProcessing);
    }
}
