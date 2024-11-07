using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.Flows.Infrastructure;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntryIndexingFlow : IndexingFlowBase<EntryIndexCursor>
{
    private const int BatchSize = 100;
    private const int Quota = 1_000;

    private Task WhenReady => Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var chatId = new ChatId(Id.Arguments);
        if (!await EnsureChatInfo(chatId, cancellationToken))
            return WaitForEvent(FlowSteps.OnReset, InfiniteHardResumeAt);

        return await base.OnReset(cancellationToken);
    }

    protected override async Task<BatchIndexingResult<EntryIndexCursor>> ProcessBatch(EntryIndexCursor? cursor, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var updateLoader = Host.Services.GetRequiredService<IChatContentUpdateLoader>();
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();
        var queues = Host.Services.Queues();
        var chatId = new ChatId(Id.Arguments);

        cursor ??= new (0, 0);
        var batches = updateLoader.LoadChatUpdatesAsync(chatId,
                cursor.LastVersion,
                cursor.LastLid,
                cancellationToken)
            .Take(Quota)
            .Chunk(BatchSize, cancellationToken)
            .ConfigureAwait(false);
        var handledCount = 0;
        await foreach (var batch in batches) {
            var updated = batch.Where(x => x is { IsRemoved: false, IsSystemEntry: false }).Select(x => x.ToIndexedEntry()).ToList();
            var removed = batch.Where(x => x is { IsRemoved: true, IsSystemEntry: false }).Select(x => x.Id.AsTextEntryId()).ToList();
            await indexedDocuments.Update(updated, removed, cancellationToken).ConfigureAwait(false);
            var lastIndexed = batch[^1];
            cursor = new EntryIndexCursor(lastIndexed.LocalId, lastIndexed.Version);
            handledCount += batch.Count;
        }
        var isTailReached = handledCount < Quota;
        if (isTailReached)
            await queues.Enqueue(new SearchBackend_Refresh(RefreshEntries: true), cancellationToken).ConfigureAwait(false);
        return new (false, isTailReached, cursor);
    }

    // Private methods

    private async Task<bool> EnsureChatInfo(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatsBackend = Host.Services.GetRequiredService<IChatsBackend>();
        var placesBackend = Host.Services.GetRequiredService<IPlacesBackend>();
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null) {
            Log.LogWarning("Unable to create chat info: chat #{ChatId} doesn't exist", chatId);
            return false;
        }

        Place? place = null;
        if (!chat.Id.PlaceId.IsNone) {
            place = await placesBackend.Get(chatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null) {
                Log.LogWarning("Unable to create chat info: place #{PlaceId} doesn't exist", chat.Id.PlaceId);
                return false;
            }
        }

        var indexedChat = chat.ToIndexedChat(place);
        await indexedDocuments.Update([indexedChat], cancellationToken).ConfigureAwait(false);
        return true;
    }
}
