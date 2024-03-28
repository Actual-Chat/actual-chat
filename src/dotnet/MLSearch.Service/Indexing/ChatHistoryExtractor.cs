using ActualChat.Chat;

using ActualChat.MLSearch.Engine.Indexing;

namespace ActualChat.MLSearch.Indexing;

internal sealed class ChatHistoryExtractor (
    ISink<ChatEntry, ChatEntry> sink,
    IChatEntryCursorStates cursorStates,
    INewChatEntryLoader newChatEntryLoader,
    IUpdatedAndRemovedEntryLoader updatedAndRemovedEntryLoader
): IDataIndexer<ChatId>
{
    public async Task<DataIndexerResult> IndexNextAsync(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await cursorStates.LoadAsync(chatId, cancellationToken)
            .ConfigureAwait(false);

        var creates = await newChatEntryLoader.LoadAsync(chatId, state, cancellationToken)
            .ConfigureAwait(false);
        var (updates, deletes) = await updatedAndRemovedEntryLoader.LoadAsync(chatId, state, cancellationToken)
            .ConfigureAwait(false);

        var lastTouchedEntry =
            creates.Concat(updates).Concat(deletes).MaxBy(e=>e.Version);

        // It can only be null if all lists are empty.
        if (lastTouchedEntry == null) {
            // This is a simple logic to determine the end of changes currently available.
            return new DataIndexerResult(IsEndReached: true);
        }
        await sink.ExecuteAsync(creates.Concat(updates), deletes, cancellationToken)
            .ConfigureAwait(false);
        var next = new ChatEntryCursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
        await cursorStates.SaveAsync(chatId, next, cancellationToken).ConfigureAwait(false);
        return new DataIndexerResult(IsEndReached: false);
    }
}

internal record ChatEntryCursor(long LastEntryLocalId, long LastEntryVersion);

internal interface IChatEntryCursorStates
{
    Task<ChatEntryCursor> LoadAsync(ChatId key, CancellationToken cancellationToken);
    Task SaveAsync(ChatId key, ChatEntryCursor state, CancellationToken cancellationToken);
}

internal class ChatEntryCursorStates(ICursorStates<ChatEntryCursor> cursorStates): IChatEntryCursorStates
{
    public async Task<ChatEntryCursor> LoadAsync(ChatId key, CancellationToken cancellationToken)
        => (await cursorStates.Load(key, cancellationToken).ConfigureAwait(false)) ?? new(0, 0);

    public async Task SaveAsync(ChatId key, ChatEntryCursor state, CancellationToken cancellationToken)
        => await cursorStates.Save(key, state, cancellationToken).ConfigureAwait(false);
}

internal interface INewChatEntryLoader
{
    Task<IReadOnlyList<ChatEntry>> LoadAsync(ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken);
}

internal class NewChatEntryLoader(IChatsBackend chats): INewChatEntryLoader
{
    public async Task<IReadOnlyList<ChatEntry>> LoadAsync(ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken)
    {
        var result = new List<ChatEntry>();
        var lastIndexedLid = cursor.LastEntryLocalId;
        var news = await chats.GetNews(chatId, cancellationToken).ConfigureAwait(false);
        var lastLid = (news.TextEntryIdRange.End - 1).Clamp(0, long.MaxValue);
        if (lastIndexedLid >= lastLid) {
            return result;
        }

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
}

internal interface IUpdatedAndRemovedEntryLoader {
    Task<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)> LoadAsync(
        ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken);
}

internal class UpdatedAndRemovedEntryLoader(IChatsBackend chats): IUpdatedAndRemovedEntryLoader
{
    private const int EntryBatchSize = 100;

    public async Task<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)> LoadAsync(
        ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken)
    {
        // Note: Copied from IndexingQueue
        var updates = new List<ChatEntry>();
        var deletes = new List<ChatEntry>();
        var maxEntryVersion = await chats.GetMaxEntryVersion(chatId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (cursor.LastEntryVersion >= maxEntryVersion)
            return (updates, deletes);

        var changedEntries = await chats
            .ListChangedEntries(chatId,
                EntryBatchSize,
                cursor.LastEntryLocalId,
                cursor.LastEntryVersion,
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
}
