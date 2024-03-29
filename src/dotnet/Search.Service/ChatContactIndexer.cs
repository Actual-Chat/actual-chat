using ActualChat.Chat;

namespace ActualChat.Search;

public sealed class ChatContactIndexer(IServiceProvider services) : ContactIndexer(services)
{
    private IChatsBackend? _chatsBackend;

    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();

    protected override async Task Sync(CancellationToken cancellationToken)
    {
        var (hasPublicChatChanges, hasPrivateChatChanges) = await SyncChanges(cancellationToken).ConfigureAwait(false);
        if (hasPublicChatChanges || hasPrivateChatChanges) {
            var cmd = new SearchBackend_Refresh(false, hasPublicChatChanges, hasPrivateChatChanges);
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(bool HasPublicChatsChanges, bool HasPrivateChatChanges)> SyncChanges(CancellationToken cancellationToken)
    {
        var state = await ContactIndexStatesBackend.GetForChats(cancellationToken).ConfigureAwait(false);
        var batches = ChatsBackend
            .BatchChanged(
                state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedChatId,
                SyncBatchSize,
                cancellationToken);
        var hasPublicChatChanges = false;
        var hasPrivateChatChanges = false;
        await foreach (var chats in batches.ConfigureAwait(false)) {
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
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            if (!hasPrivateChatChanges)
                hasPrivateChatChanges = updates.Any(x => !x.IsPublic);
            if (!hasPublicChatChanges)
                hasPublicChatChanges = updates.Any(x => x.IsPublic);
        }
        return (hasPublicChatChanges, hasPrivateChatChanges);

        async Task<Dictionary<PlaceId, Place>> GetPlaceMap(ApiArray<Chat.Chat> chats)
        {
            var places = await chats.Where(x => x.Id.IsPlaceChat)
                .Select(x => x.Id.PlaceId)
                .Distinct()
                .Select(x => ChatsBackend.GetPlace(x, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            return places.SkipNullItems().ToDictionary(x => x.Id);
        }
    }
}
