using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class EntryIndexingFlow : BatchedIndexingFlowBase<ChatEntry, ChatEntryId>
{
    protected override int CurrentFlowSetVersion => 1;
    private Task WhenReady => Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;

    protected override async Task<bool> OnBeforeFirstIndexAfterReset(CancellationToken cancellationToken)
        => await base.OnBeforeFirstIndexAfterReset(cancellationToken)
            && await EnsureChatInfo(new ChatId(Id.Arguments), cancellationToken);

    protected override async Task<IReadOnlyList<ChatEntry>> GetBatch(
        IndexingFlowCursor<ChatEntryId>? cursor,
        CancellationToken cancellationToken)
    {
        // TODO: maxVersion = now - IndexingDelay
        var updateLoader = Host.Services.GetRequiredService<IChatContentUpdateLoader>();
        cursor ??= new (ChatEntryId.None, 0);
        return await updateLoader.LoadChatUpdatesAsync(new ChatId(Id.Arguments),
                cursor.LastUpdatedVersion,
                cursor.LastUpdatedId.LocalId,
                cancellationToken)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task ProcessBatch(IReadOnlyList<ChatEntry> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();

        var updated = batch.Where(x => x is { IsRemoved: false, IsSystemEntry: false }).Select(x => x.ToIndexedEntry()).ToList();
        var removed = batch.Where(x => x is { IsRemoved: true, IsSystemEntry: false }).Select(x => x.Id.AsTextEntryId()).ToList();
        await indexedDocuments.UpdateEntries(updated, removed, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(int processCount, CancellationToken cancellationToken)
    {
        var transitionKind = await base.HandleTail(processCount, cancellationToken).ConfigureAwait(false);
        if (processCount > 0) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting entry index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshEntries: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transitionKind;
    }

    // Private methods

    private async Task<bool> EnsureChatInfo(ChatId chatId, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
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
        await indexedDocuments.UpdateChats([indexedChat], cancellationToken).ConfigureAwait(false);
        return true;
    }
}
