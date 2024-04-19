using ActualChat.Chat;

namespace ActualChat.MLSearch.Indexing;

internal sealed class ChatHistoryExtractor (
    ISink<ChatEntry, ChatEntryId> sink,
    IChatsBackend chats,
    ICursorStates<ChatHistoryExtractor.Cursor> cursorStates
): IDataIndexer<ChatId>
{
    private const int EntryBatchSize = 100;

    public async Task<DataIndexerResult> IndexNextAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await cursorStates.Load(
            IdOf(chatId),
            cancellationToken
            )
            .ConfigureAwait(false);
        state ??= NewFor(chatId);
        var creates = await ListNewEntries(chatId, state, cancellationToken)
            .ConfigureAwait(false);
        var (updates, deletes) = await ListUpdatedAndRemovedEntries(chatId, state, cancellationToken)
            .ConfigureAwait(false);

        // TODO: Ask @frol
        // - If an entry was added to a chat would it have larger version than every other existing item there?
        var lastTouchedEntry =
            creates.Concat(updates).Concat(deletes).MaxBy(e=>e.Version);
        // -- End of logic that depends on the answer above

        // It can only be null if all lists are empty.
        if (lastTouchedEntry == null) {
            // This is a simple logic to determine the end of changes currently available.
            return new DataIndexerResult(IsEndReached: true);
        }
        await sink.ExecuteAsync(creates.Concat(updates), deletes.Select(e => e.Id), cancellationToken)
            .ConfigureAwait(false);
        var next = new Cursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
        await cursorStates.Save(IdOf(chatId), next, cancellationToken).ConfigureAwait(false);
        return new DataIndexerResult(IsEndReached: false);
    }

    private async Task<IList<ChatEntry>> ListNewEntries(ChatId chatId, Cursor cursor, CancellationToken cancellationToken)
    {
        // Note: Copied from IndexingQueue
        var result = new List<ChatEntry>();
        var lastIndexedLid = cursor.LastEntryLocalId;
        var news = await chats.GetNews(chatId, cancellationToken).ConfigureAwait(false);
        var lastLid = (news.TextEntryIdRange.End - 1).Clamp(0, long.MaxValue);
        if (lastIndexedLid >= lastLid)
            return result;

        var idTiles =
            Constants.Chat.ServerIdTileStack.LastLayer.GetCoveringTiles(
                news.TextEntryIdRange.WithStart(lastIndexedLid));
        foreach (var tile in idTiles) {
            var chatTile = await chats.GetTile(chatId,
                    ChatEntryKind.Text,
                    tile.Range,
                    false,
                    cancellationToken)
                .ConfigureAwait(false);
            result.AddRange(chatTile.Entries.Where(x => !x.Content.IsNullOrEmpty()));
        }
        return result;
    }

    private async Task<(IList<ChatEntry> updates, IList<ChatEntry> deletes)> ListUpdatedAndRemovedEntries(ChatId chatId, Cursor cursor, CancellationToken cancellationToken)
    {
        // Note: Copied from IndexingQueue
        var updates = new List<ChatEntry>();
        var deletes = new List<ChatEntry>();
        var maxEntryVersion = await chats.GetMaxEntryVersion(chatId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (cursor.LastEntryVersion >= maxEntryVersion)
            return (updates, deletes);

        var changedEntries = await chats
            .ListChangedEntries(chatId,
                cursor.LastEntryLocalId,
                cursor.LastEntryVersion,
                EntryBatchSize,
                cancellationToken)
            .ConfigureAwait(false);
        updates.AddRange(
            changedEntries.Where(x => !x.IsRemoved && !x.Content.IsNullOrEmpty())
        );
        deletes.AddRange(
            changedEntries.Where(x => x.IsRemoved || x.Content.IsNullOrEmpty())
        );
        return (updates, deletes);
    }

    private static Symbol IdOf(in ChatId chatId) => chatId.Id;

    private static Cursor NewFor(in ChatId _)
        => new (0, 0);

    internal record Cursor(long LastEntryLocalId, long LastEntryVersion);
}
