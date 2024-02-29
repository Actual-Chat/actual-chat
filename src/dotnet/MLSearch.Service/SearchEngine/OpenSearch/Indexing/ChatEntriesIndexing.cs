using ActualChat.Chat;
using ActualChat.MLSearch.SearchEngine.OpenSearch.Indexing;
using ActualChat.MLSearch.SearchEngine.OpenSearch.Indexing.Spout;
using ActualChat.MLSearch.SearchEngine.OpenSearch.Stream;
using OpenSearch.Client;

namespace ActualChat.MLSearch.ApiAdapters;

// A stream represent a sequence of:
// - Spout: Subscribes to whatever events it needs
//   and maps events into an internal set of commands.
// -


internal class ChatEntriesIndexing(
    IChatsBackend chats,
    IndexingCursors<ChatEntriesIndexing.Cursor> cursors,
    Sink<ChatEntry, ChatEntry> sink,
    ILoggerSource loggerSource
)
{
    private const int EntryBatchSize = 100;
    private const int ChannelCapacity = 10;
    private ILogger? _log;
    private ILogger Log => _log ??= loggerSource.GetLogger(GetType());

    private Channel<MLSearch_TriggerContinueChatIndexing>? _channel;
    private IChatsBackend Chats => chats;
    private Sink<ChatEntry, ChatEntry> Sink => sink;
    private IndexingCursors<Cursor> Cursors => cursors;

    protected virtual Channel<MLSearch_TriggerContinueChatIndexing> TriggersChannel {
        get {
            _channel ??= Channel.CreateBounded<MLSearch_TriggerContinueChatIndexing>(ChannelCapacity);
            return _channel;
        }
    }

    public ChannelWriter<MLSearch_TriggerContinueChatIndexing> Trigger => TriggersChannel.Writer;

    private async Task<IList<ChatEntry>> ListNewEntries(ChatId chatId, Cursor cursor, CancellationToken cancellationToken)
    {
        // Note: Copied from IndexingQueue
        var result = new List<ChatEntry>();
        var lastIndexedLid = cursor.LastEntryLocalId;
        var news = await Chats.GetNews(chatId, cancellationToken).ConfigureAwait(false);
        var lastLid = (news.TextEntryIdRange.End - 1).Clamp(0, long.MaxValue);
        if (lastIndexedLid >= lastLid)
            return result;

        var idTiles =
            Constants.Chat.ServerIdTileStack.LastLayer.GetCoveringTiles(
                news.TextEntryIdRange.WithStart(lastIndexedLid));
        foreach (var tile in idTiles) {
            var chatTile = await Chats.GetTile(chatId,
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

        var changedEntries = await Chats
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

    private async Task<IndexingResult> IndexNext(ChatId chatId, CancellationToken cancellationToken)
    {
        var state = await Cursors.Load(
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
        // - If an entry was added to a chat whould it have larger version than every other existing item there?
        var lastTouchedEntry =
            creates.Concat(updates).Concat(deletes).MaxBy(e=>e.Version);
        // -- End of logic that depends on the answer above

        // It can only be null if all lists are empty.
        if (lastTouchedEntry == null) {
            // This is a simple logic to determine the end of changes currently available.
            return new IndexingResult(IsEndReached: true);
        }
        await Sink.Execute(creates.Concat(updates), deletes, cancellationToken)
            .ConfigureAwait(false);
        var next = new Cursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
        await Cursors.Save(IdOf(chatId), next, cancellationToken).ConfigureAwait(false);
        return new IndexingResult(IsEndReached: false);
    }

    public async Task Execute(int shardIndex, CancellationToken cancellationToken)
    {
        // This method is a single unit of work.
        // As far as I understood there's an embedded assumption made
        // that it is possible to rehash shards attached to the host
        // between OnRun method executions.
        //
        // We calculate stream cursor each call to prevent
        // issues in case of re-sharding or new cluster rollouts.
        // It might have some other concurrent worker has updated
        // a cursor. TLDR: prevent stale cursor data.
        await foreach (
            // TODO: Make unique.
            var e in TriggersChannel
                .Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false)
        ) {
            var result = await IndexNext(e.Id, cancellationToken).ConfigureAwait(false);
            if (!result.IsEndReached) {
                // Enqueue event to continue indexing.
                if (!Trigger.TryWrite(new MLSearch_TriggerContinueChatIndexing(e.Id))) {
                    Log.LogWarning("Event queue is full: We can't process till this indexing is fully complete.");
                    while (!result.IsEndReached) {
                        result = await IndexNext(e.Id, cancellationToken).ConfigureAwait(false);
                    }
                    Log.LogWarning("Event queue is full: exiting an element.");
                }
            }
        }
    }

    private Id IdOf(ChatId chatId) => new (chatId);

    private Cursor NewFor(ChatId chatId)
        => new (0, 0);

    internal record IndexingResult(bool IsEndReached);

    internal record Cursor(long LastEntryLocalId, long LastEntryVersion);

}
