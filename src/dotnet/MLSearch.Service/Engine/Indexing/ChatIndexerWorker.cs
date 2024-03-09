using System.Threading.Tasks.Dataflow;
using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.DataFlow;
using TInput = (ActualChat.ChatEntryId Id, ActualChat.ChangeKind ChangeKind);
using TOutput = (
    System.Collections.Generic.IEnumerable<ActualChat.Chat.ChatEntry>?,
    System.Collections.Generic.IEnumerable<ActualChat.Chat.ChatEntry>?
);

namespace ActualChat.MLSearch.Engine.Indexing;

internal interface IChatIndexerWorker: ISpout<(ChatEntryId, ChangeKind)>
{
    Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken);
}

internal class ChatIndexerWorker : IChatIndexerWorker
{
    [StructLayout(LayoutKind.Auto)]
    private readonly struct Content<TPayload>
    {
        public static Content<TPayload> None = new();
        public readonly ChatId Id { get; }
        private readonly TPayload? _payload;
        public readonly TPayload Payload
            => _hasPayload ? _payload! : throw new InvalidOperationException("Container is empty.");
        private readonly bool _hasPayload;
        public readonly bool IsEmpty => !_hasPayload;
        public Content(ChatId id) => (Id, _payload, _hasPayload) = (id, default, false);
        public Content(ChatId id, TPayload payload) => (Id, _payload, _hasPayload) = (id, payload, true);
    };
    private record class Envelop<TPayload>(Content<TPayload> Payload, CancellationToken CancellationToken);
    private record class JobMetadata(CancellationTokenSource CancellationSource);
    private const int EntryBatchSize = 100;
    private const int ChannelCapacity = 10;
    private readonly IChatsBackend _chats;
    private readonly ICursorStates<Cursor> _cursorStates;
    private readonly ISink<TOutput> _sink;
    private readonly ILoggerSource _loggerSource;
    private readonly BufferBlock<TInput> _inputBlock;
    private readonly TransformBlock<TInput, Envelop<TInput>> _initBlock;
    private readonly ActionBlock<Envelop<Unit>> _completeBlock;
    private readonly ConcurrentDictionary<ChatId, JobMetadata> _runningJobs;
    private ILogger? _log;
    private ILogger Log => _log ??= _loggerSource.GetLogger(GetType());

    public ChatIndexerWorker(
        IChatsBackend chats,
        ICursorStates<Cursor> cursorStates,
        ISink<(IEnumerable<ChatEntry>?, IEnumerable<ChatEntry>?)> sink,
        ILoggerSource loggerSource
    )
    {
        _chats = chats;
        _cursorStates = cursorStates;
        _sink = sink;
        _loggerSource = loggerSource;

        _inputBlock = new(new() {
            BoundedCapacity = ChannelCapacity,
        });

        _initBlock = new(Init);

        _completeBlock = new(Complete);

        _sinkBlock = new(item => _sink.ExecuteAsync(item, default));
    }

    private Envelop<TInput> Init(TInput input)
    {
        var (jobId, cancellationSource) = (input.Id.ChatId, new CancellationTokenSource());
        if (_runningJobs.TryAdd(jobId, new(cancellationSource))) {
            return new(new Content<TInput>(jobId, input), cancellationSource.Token);
        }
        // We do not start indexing of the same chat in parallel
        cancellationSource.DisposeSilently();
        return new(Content<TInput>.None, default);
    }
    private void Complete(Envelop<Unit> envelop)
    {
        var jobId = envelop.Payload.Id;
        if (_runningJobs.TryRemove(jobId, out var jobMetadata)) {
            // TODO: Log and Trace
            jobMetadata.CancellationSource.DisposeSilently();
        }
    }

    public async Task PostAsync((ChatEntryId, ChangeKind) input, CancellationToken cancellationToken)
        => await _inputBlock.SendAsync(input, cancellationToken).ConfigureAwait(false);

    private async Task<IList<ChatEntry>> ListNewEntries(ChatId chatId, Cursor cursor, CancellationToken cancellationToken)
    {
        // Note: Copied from IndexingQueue
        var result = new List<ChatEntry>();
        var lastIndexedLid = cursor.LastEntryLocalId;
        var news = await _chats.GetNews(chatId, cancellationToken).ConfigureAwait(false);
        var lastLid = (news.TextEntryIdRange.End - 1).Clamp(0, long.MaxValue);
        if (lastIndexedLid >= lastLid)
            return result;

        var idTiles =
            Constants.Chat.ServerIdTileStack.LastLayer.GetCoveringTiles(
                news.TextEntryIdRange.WithStart(lastIndexedLid));
        foreach (var tile in idTiles) {
            var chatTile = await _chats.GetTile(chatId,
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
        var maxEntryVersion = await _chats.GetMaxEntryVersion(chatId, cancellationToken).ConfigureAwait(false) ?? 0;
        if (cursor.LastEntryVersion >= maxEntryVersion)
            return (updates, deletes);

        var changedEntries = await _chats
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
        var cursor = await _cursorStates.Load(
            IdOf(chatId),
            cancellationToken
            )
            .ConfigureAwait(false);
        cursor ??= NewFor(chatId);
        var creates = await ListNewEntries(chatId, cursor, cancellationToken)
            .ConfigureAwait(false);
        var (updates, deletes) = await ListUpdatedAndRemovedEntries(chatId, cursor, cancellationToken)
            .ConfigureAwait(false);

        // TODO: Ask @frol
        // - If an entry was added to a chat would it have larger version than every other existing item there?
        var lastTouchedEntry =
            creates.Concat(updates).Concat(deletes).MaxBy(e=>e.Version);
        // -- End of logic that depends on the answer above

        // It can only be null if all lists are empty.
        if (lastTouchedEntry == null) {
            // This is a simple logic to determine the end of changes currently available.
            return new IndexingResult(IsEndReached: true);
        }
        await _sink.ExecuteAsync((creates.Concat(updates), deletes), cancellationToken)
            .ConfigureAwait(false);
        var next = new Cursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
        await _cursorStates.Save(IdOf(chatId), next, cancellationToken).ConfigureAwait(false);
        return new IndexingResult(IsEndReached: false);
    }

    private async Task<IndexingResult> IndexChatTail(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        return await IndexNext(entryId.ChatId, cancellationToken).ConfigureAwait(false);
    }
    private async Task<IndexingResult> IndexChatRange(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        // Fall back to chat tail indexing
        return await IndexChatTail(entryId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken)
    {
        // Infinitely wait for cancellation while dataflow pipeline processes
        // chat index requests
        await Task.Delay(TimeSpan.MaxValue, cancellationToken).ConfigureAwait(false);

        // Complete input channel so it doesn't accept input messages anymore
        // and also it propagates completion through the pipeline.
        _inputBlock.Complete();

        await _sinkBlock.Completion.ConfigureAwait(false);

        // // This method is a single unit of work.
        // // As far as I understood there's an embedded assumption made
        // // that it is possible to rehash shards attached to the host
        // // between OnRun method executions.
        // //
        // // We calculate stream cursor each call to prevent
        // // issues in case of re-sharding or new cluster rollouts.
        // // It might have some other concurrent worker has updated
        // // a cursor. TLDR: prevent stale cursor data.
        // while (!cancellationToken.IsCancellationRequested) {
        //     try {
        //         var e = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        //         var result = await (e.ChangeKind switch {
        //             ChangeKind.Create => IndexChatTail(e.Id, cancellationToken),
        //             _ => IndexChatRange(e.Id, cancellationToken)
        //         }).ConfigureAwait(false);

        //         if (!result.IsEndReached) {
        //             // Enqueue event to continue indexing.
        //             if (!_channel.Writer.TryWrite((e.Id, ChangeKind.Create))) {
        //                 Log.LogWarning("Event queue is full: We can't process till this indexing is fully complete.");
        //                 while (!result.IsEndReached) {
        //                     result = await IndexChatTail(e.Id, cancellationToken).ConfigureAwait(false);
        //                 }
        //                 Log.LogWarning("Event queue is full: exiting an element.");
        //             }
        //         }
        //     }
        //     catch (Exception e) {
        //         Log.LogError(e.Message);
        //         throw;
        //     }
        // }
    }

    private static Symbol IdOf(in ChatId chatId) => chatId.Id;

    private static Cursor NewFor(in ChatId _)
        => new (0, 0);

    internal record IndexingResult(bool IsEndReached);

    internal record Cursor(long LastEntryLocalId, long LastEntryVersion);
}
