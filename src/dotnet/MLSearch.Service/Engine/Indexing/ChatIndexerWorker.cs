using System.Threading.Tasks.Dataflow;
using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.DataFlow;
using TPipelineInput = (ActualChat.ChatEntryId Id, ActualChat.ChangeKind ChangeKind);
using TPipelineOutput = (
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
    private record class Envelope<TPayload>(Content<TPayload> Content, CancellationToken CancellationToken);
    private record class JobMetadata(CancellationTokenSource CancellationSource);
    private const int EntryBatchSize = 100;
    private const int ChannelCapacity = 10;
    private readonly IChatsBackend _chats;
    private readonly ICursorStates<ChatEntryCursor> _cursorStates;
    private readonly ISink<TPipelineOutput> _sink;
    private readonly ILoggerSource _loggerSource;
    private readonly BufferBlock<TPipelineInput> _inputBlock;
    private readonly TransformBlock<TPipelineInput, Envelope<TPipelineInput>> _initBlock;
    private readonly ActionBlock<Envelope<Unit>> _completeBlock;
    private readonly ConcurrentDictionary<ChatId, JobMetadata> _runningJobs;
    private ILogger? _log;
    private ILogger Log => _log ??= _loggerSource.GetLogger(GetType());

    public ChatIndexerWorker(
        IChatsBackend chats,
        IChatEntryCursorStates cursorStates,
        ISink<(IEnumerable<ChatEntry>?, IEnumerable<ChatEntry>?)> sink,
        ILoggerSource loggerSource
    )
    {
        _chats = chats;
        _cursorStates = cursorStates;
        _sink = sink;
        _loggerSource = loggerSource;

        _runningJobs = new();

        _inputBlock = new(new() {
            BoundedCapacity = ChannelCapacity,
        });

        _initBlock = new(Init);

        _completeBlock = new(Complete);

        var sinkBlock = new TransformBlock<Envelope<TPipelineOutput>, Envelope<Unit>>(
            item => SinkAsync(item, _sink));
    }

    private Envelope<TPipelineInput> Init(TPipelineInput input)
    {
        var (jobId, cancellationSource) = (input.Id.ChatId, new CancellationTokenSource());
        if (_runningJobs.TryAdd(jobId, new(cancellationSource))) {
            return new(new Content<TPipelineInput>(jobId, input), cancellationSource.Token);
        }
        // We do not start indexing of the same chat in parallel
        cancellationSource.DisposeSilently();
        return new(Content<TPipelineInput>.None, default);
    }
    private Task<Envelope<TOutput>> TransformAsync<TInput, TOutput>(
        Envelope<TInput> envelope, ITransform<TInput,TOutput> transform)
        => TransformAsync(envelope, transform.ExecuteAsync);

    private Task<Envelope<Unit>> SinkAsync<TInput>(Envelope<TInput> envelope, ISink<TInput> sink)
        => TransformAsync(envelope, async (input, cancellationToken) => {
            await sink.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            return Unit.Default;
        });

    private async Task<Envelope<TOutput>> TransformAsync<TInput, TOutput>(
        Envelope<TInput> envelope, Func<TInput,CancellationToken,Task<TOutput>> transform)
    {
        var (jobId, cancellationToken) = (envelope.Content.Id, envelope.CancellationToken);
        if (!envelope.Content.IsEmpty && !cancellationToken.IsCancellationRequested) {
            try {
                var result = await transform(envelope.Content.Payload, cancellationToken).ConfigureAwait(false);
                return new Envelope<TOutput>(new Content<TOutput>(jobId, result), cancellationToken);
            }
            catch (OperationCanceledException) {
                Log.LogInformation($"Operation {jobId} is cancelled.");
            }
            catch (Exception e) {
                Log.LogError(e, $"Operation {jobId} is failed.");
            }
        }
        // Just propagate emply envelop via pipeline
        return new Envelope<TOutput>(new Content<TOutput>(jobId), cancellationToken);
    }

    private void Complete(Envelope<Unit> envelope)
    {
        var jobId = envelope.Content.Id;
        if (_runningJobs.TryRemove(jobId, out var jobMetadata)) {
            // TODO: Log and Trace
            jobMetadata.CancellationSource.DisposeSilently();
        }
    }

    public async Task PostAsync((ChatEntryId, ChangeKind) input, CancellationToken cancellationToken)
        => await _inputBlock.SendAsync(input, cancellationToken).ConfigureAwait(false);

    private async Task<IList<ChatEntry>> ListNewEntries(ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken)
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

    private async Task<(IList<ChatEntry> updates, IList<ChatEntry> deletes)> ListUpdatedAndRemovedEntries(ChatId chatId, ChatEntryCursor cursor, CancellationToken cancellationToken)
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
        var next = new ChatEntryCursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
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

    private static ChatEntryCursor NewFor(in ChatId _)
        => new (0, 0);

    internal record IndexingResult(bool IsEndReached);

}

internal record ChatEntryCursor(long LastEntryLocalId, long LastEntryVersion);

internal interface IChatEntryCursorStates
    : ITransform<ChatId, ChatEntryCursor>, ISink<(ChatId, ChatEntryCursor)>;

internal class ChatEntryCursorStates(ICursorStates<ChatEntryCursor> cursorStates): IChatEntryCursorStates
{
    async Task<ChatEntryCursor> ITransform<ChatId, ChatEntryCursor>.ExecuteAsync(ChatId key, CancellationToken cancellationToken)
        => (await cursorStates.Load(key, cancellationToken).ConfigureAwait(false)) ?? new(0, 0);

    async Task ISink<(ChatId, ChatEntryCursor)>.ExecuteAsync((ChatId, ChatEntryCursor) input, CancellationToken cancellationToken)
    {
        var (key, state) = input;
        await cursorStates.Save(key, state, cancellationToken).ConfigureAwait(false);
    }
}

internal interface INewChatEntryLoader: ITransform<(ChatId, ChatEntryCursor), IReadOnlyList<ChatEntry>>;

internal class NewChatEntryLoader(IChatsBackend chats): INewChatEntryLoader
{
    public async Task<IReadOnlyList<ChatEntry>> ExecuteAsync((ChatId, ChatEntryCursor) input, CancellationToken cancellationToken)
    {
        var (chatId, cursor) = input;
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

internal interface IUpdatedAndRemovedEntryLoader
    : ITransform<(ChatId, ChatEntryCursor), (IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)>;

internal class UpdatedAndRemovedEntryLoader(IChatsBackend chats): IUpdatedAndRemovedEntryLoader
{
    private const int EntryBatchSize = 100;

    public async Task<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)> ExecuteAsync(
        (ChatId, ChatEntryCursor) input, CancellationToken cancellationToken)
    {
        var (chatId, cursor) = input;
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
