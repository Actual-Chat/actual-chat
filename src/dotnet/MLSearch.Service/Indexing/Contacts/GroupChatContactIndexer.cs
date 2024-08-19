using ActualChat.Chat;
using ActualChat.Search;

namespace ActualChat.MLSearch.Indexing;

public sealed class GroupChatContactIndexer(IServiceProvider services) : ContactIndexer(services)
{
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();

    protected override async Task Sync(CancellationToken cancellationToken)
    {
        var hasChanges = await SyncChanges(cancellationToken).ConfigureAwait(false);
        if (hasChanges) {
            var cmd = new SearchBackend_Refresh(refreshGroups: hasChanges);
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SyncChanges(CancellationToken cancellationToken)
    {
        var state = await ContactIndexStatesBackend.GetForChats(cancellationToken).ConfigureAwait(false);
        var batches = ChatsBackend
            .BatchChangedGroups(
                state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedChatId,
                SyncBatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        var hasChanges = false;
        await foreach (var chats in batches) {
            var first = chats[0];
            var last = chats[^1];
            Log.LogDebug(
                "Indexing {BatchSize} chats [(v={FirstVersion}, #{FirstId})..(v={LastVersion}, #{LastId})]",
                chats.Count,
                first.Version,
                first.Id,
                last.Version,
                last.Id);
            NeedsSync.Reset();
            var placeMap = await GetPlaceMap(chats).ConfigureAwait(false);
            var updates = chats.Select(x => x.ToIndexedChatContact(placeMap.GetValueOrDefault(x.Id.PlaceId)))
                .ToApiArray();
            var indexCmd = new SearchBackend_ChatContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken).ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            hasChanges |= updates.Count > 0;
        }
        return hasChanges;

        async Task<Dictionary<PlaceId, Place>> GetPlaceMap(ApiArray<Chat.Chat> chats)
        {
            var places = await chats.Where(x => x.Id.IsPlaceChat)
                .Select(x => x.Id.PlaceId)
                .Distinct()
                .Select(x => PlacesBackend.Get(x, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            return places.SkipNullItems().ToDictionary(x => x.Id);
        }
    }
}
