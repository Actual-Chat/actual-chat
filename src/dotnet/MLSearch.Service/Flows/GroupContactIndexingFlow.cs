using ActualChat.Chat;
using ActualChat.Flows;
using ActualChat.MLSearch.Engine.OpenSearch.Indexing;
using ActualChat.MLSearch.Module;
using ActualChat.Queues;
using ActualChat.Search;
using MemoryPack;

namespace ActualChat.MLSearch.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class GroupContactIndexingFlow : BatchedIndexingFlowBase<Chat.Chat, ChatId>, IMasterFlow
{
    protected override int CurrentFlowSetVersion => 1;
    protected override TimeSpan RecheckInterval => Host.Services.GetRequiredService<MLSearchSettings>().IndexingTailRecheckInterval;
    [field: AllowNull, MaybeNull]
    private Task WhenReady => field ??= Host.Services.GetRequiredService<OpenSearchConfigurator>().WhenCompleted;
    [field: AllowNull, MaybeNull]
    private MLSearchSettings Settings => field ??= Host.Services.GetRequiredService<MLSearchSettings>();

    protected override async Task<IReadOnlyList<Chat.Chat>> GetBatch(
        IndexingFlowCursor<ChatId>? cursor,
        CancellationToken cancellationToken)
    {
        var chatsBackend = Host.Services.GetRequiredService<IChatsBackend>();
        var maxVersion = Clocks.GetMaxVersion(Settings.ChangedEntityIndexingDelay);
        cursor ??= new (ChatId.None, 0);
        var batch = await chatsBackend.ListChanged(
                new () {
                    MinVersion = cursor.LastUpdatedVersion,
                    MaxVersion = maxVersion,
                    LastId = cursor.LastUpdatedId,
                    Limit = BatchSize,
                    ExcludePeerChats = true,
                    ExcludePlaceRootChats = true,
                },
                cancellationToken)
            .ConfigureAwait(false);
        Log.LogDebug("`{Id}`.GetBatch: retrieved {Count} items with maxVersion={MaxVersion}, cursor={Cursor}", Id, batch.Count, maxVersion, cursor);
        return batch;
    }

    protected override async Task ProcessBatch(IReadOnlyList<Chat.Chat> batch, CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var indexedDocuments = Host.Services.GetRequiredService<IndexedDocuments>();

        var placeMap = await GetPlaceMap(batch, cancellationToken).ConfigureAwait(false);
        var updated = batch.Select(x => x.ToIndexedGroupContact(placeMap.GetValueOrDefault(x.Id.PlaceId))).ToApiArray();
        await indexedDocuments.UpdateGroupContacts(updated, [], cancellationToken).ConfigureAwait(false);
    }

    protected override async Task<IndexingFlowTransitionKind> HandleTail(int processedCount, CancellationToken cancellationToken)
    {
        var transition = await base.HandleTail(processedCount, cancellationToken).ConfigureAwait(false);
        if (processedCount > 0) {
            Log.LogInformation("`{Id}`.OnTailReached: requesting group index refresh", Id);
            await Host.Services.Queues()
                .Enqueue(new SearchBackend_Refresh(RefreshGroups: true), cancellationToken)
                .ConfigureAwait(false);
        }
        return transition;
    }

    private async Task<Dictionary<PlaceId, Place>> GetPlaceMap(IReadOnlyList<Chat.Chat> chats, CancellationToken cancellationToken) {
        var placesBackend = Host.Services.GetRequiredService<IPlacesBackend>();
        var places = await chats.Where(x => x.Id.IsPlaceChat)
            .Select(x => x.Id.PlaceId)
            .Distinct()
            .Select(x => placesBackend.Get(x, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return places.SkipNullItems().ToDictionary(x => x.Id);
    }
}
