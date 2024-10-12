using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.Queues;
using ActualChat.Search;

namespace ActualChat.MLSearch.Indexing.ChatContent;

// TODO: remove cause it's replaced with EntryIndexingFlow
internal sealed class ChatEntryIndexWorker(
    IChatContentUpdateLoader updateLoader,
    ICursorStates<ChatEntryCursor> cursorStates,
    IChatsBackend chatsBackend,
    IPlacesBackend placesBackend,
    IndexedDocuments indexedDocuments,
    IQueues queues,
    MomentClockSet clocks
) :  IWorker<MLSearch_TriggerChatIndexing>
{
    private const int BatchSize = 100;
    private const int Quota = 1_000;

    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var (chatId, indexingKind) = job;
        if (indexingKind != IndexingKind.ChatEntries)
            return;

        // ensures chat info is created
        if (!await IndexChat(chatId, cancellationToken).ConfigureAwait(false))
            return;

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false)
            ?? new ((clocks.CoarseCpuClock.Now - TimeSpan.FromSeconds(5)).EpochOffset.Ticks, 0);
        var batches = updateLoader.LoadChatUpdatesAsync(chatId,
                cursor.LastEntryVersion,
                cursor.LastEntryLocalId,
                cancellationToken)
            .Take(Quota)
            .Chunk(BatchSize, cancellationToken)
            .ConfigureAwait(false);
        var handledCount = 0;
        await foreach (var batch in batches) {
            var updated = batch.Where(x => !x.IsRemoved).Select(x => x.ToIndexedEntry()).ToList();
            var removed = batch.Where(x => x.IsRemoved).Select(x => x.Id.AsTextEntryId()).ToList();
            await indexedDocuments.Update(updated, removed, cancellationToken).ConfigureAwait(false);
            var newCursor = new ChatEntryCursor(batch[^1]);
            await cursorStates.SaveAsync(chatId, newCursor, cancellationToken).ConfigureAwait(false);
            handledCount += batch.Count;
        }
        if (handledCount > 0)
            await queues.Enqueue(new SearchBackend_Refresh(RefreshEntries: true), cancellationToken).ConfigureAwait(false);

        if (handledCount < Quota) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(chatId);
            await queues.Enqueue(completionNotification, cancellationToken).ConfigureAwait(false);
        }
        else
            await queues.Enqueue(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IndexChat(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return false;

        Place? place = null;
        if (!chat.Id.PlaceId.IsNone) {
            place = await placesBackend.Get(chatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return false;
        }

        var indexedChat = chat.ToIndexedChat(place);
        await indexedDocuments.Update([indexedChat], cancellationToken).ConfigureAwait(false);
        return true;
    }
}
