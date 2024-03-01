using ActualChat.Chat;

namespace ActualChat.Search;

public class ChatContactIndexer(IServiceProvider services) : ContactIndexer(services)
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
                ApiSet.New(state.LastUpdatedChatId),
                SyncBatchSize,
                cancellationToken);
        var hasPublicChatChanges = false;
        var hasPrivateChatChanges = false;
        await foreach (var chats in batches.ConfigureAwait(false)) {
            NeedsSync.Reset();
            var updates = chats.Select(x => x.ToIndexedChatContact()).ToApiArray();
            var indexCmd = new SearchBackend_ChatContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken)
                .ConfigureAwait(false);

            var last = chats[^1];
            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            if (!hasPrivateChatChanges)
                hasPrivateChatChanges = updates.Any(x => !x.IsPublic);
            if (!hasPublicChatChanges)
                hasPublicChatChanges = updates.Any(x => x.IsPublic);
        }
        return (hasPublicChatChanges, hasPrivateChatChanges);
    }
}
