using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatIndexerWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal sealed class ChatIndexerWorker(
    int batchSize,
    int maxEventCount,
    IChatsBackend chats,
    IChatEntryCursorStates cursorStates,
    IChatIndexerFactory indexerFactory,
    ICommander commander
) : IChatIndexerWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.Id;

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false);

        var indexer = indexerFactory.Create();
        await indexer.InitAsync(cursor, cancellationToken).ConfigureAwait(false);

        await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
            await indexer.ApplyAsync(entry, cancellationToken).ConfigureAwait(false);
            if (++eventCount % batchSize == 0) {
                await FlushAsync().ConfigureAwait(false);
            }
            if (eventCount==maxEventCount) {
                break;
            }
        }

        await FlushAsync().ConfigureAwait(false);

        if (eventCount==maxEventCount) {
            await commander.Call(job, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(job.Id);
            await commander.Call(completionNotification, cancellationToken).ConfigureAwait(false);
        }
        return;

        async Task FlushAsync()
        {
            var newCursor = await indexer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await cursorStates.SaveAsync(chatId, newCursor, cancellationToken).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatEntryCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
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
