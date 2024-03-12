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
    private record class PipelineOutput(ChatId ChatId, (IReadOnlyList<ChatEntry> New, IReadOnlyList<ChatEntry> Updated, IReadOnlyList<ChatEntry> Removed) Entries);

    private record class JobMetadata(CancellationTokenSource CancellationSource);
    private const int ChannelCapacity = 10;
    private readonly IChatsBackend _chats;
    private readonly IChatEntryCursorStates _cursorStates;
    private readonly ISink<TPipelineOutput> _sink;
    private readonly ILoggerSource _loggerSource;
    private readonly BufferBlock<TPipelineInput> _inputBlock;
    private readonly TransformBlock<TPipelineInput, Envelope<TPipelineInput>> _initBlock;
    private readonly ActionBlock<Envelope<IndexingResult>> _completeBlock;
    private readonly ConcurrentDictionary<ChatId, JobMetadata> _runningJobs;
    private ILogger? _log;
    private ILogger Log => _log ??= _loggerSource.GetLogger(GetType());

    public ChatIndexerWorker(
        IChatsBackend chats,
        IChatEntryCursorStates cursorStates,
        INewChatEntryLoader newEntriesLoader,
        IUpdatedAndRemovedEntryLoader entryUpdatesLoader,
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

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

        _initBlock = new(Init);
        var getChatId = new TransformBlock<Envelope<TPipelineInput>, Envelope<ChatId>>(
            envelope => ExecuteTransformAsync(envelope, (input, _) => Task.FromResult(input.Id.ChatId)));
        var broadcastChatId = new BroadcastBlock<Envelope<ChatId>>(chatId => chatId);
        var loadCursor = new TransformBlock<Envelope<ChatId>, Envelope<ChatEntryCursor>>(
            envelope => TransformAsync(envelope, _cursorStates));
        var joinChatIdAndCursor = new JoinBlock<Envelope<ChatId>, Envelope<ChatEntryCursor>>();
        var envelopeChatIdAndCursor = new TransformBlock<Tuple<Envelope<ChatId>,Envelope<ChatEntryCursor>>, Envelope<(ChatId, ChatEntryCursor)>>(
            input => {
                var (chatIdEnvelope, cursorEnvelope) = (input.Item1, input.Item2);
                var payload = (chatIdEnvelope.Content.Payload, cursorEnvelope.Content.Payload);
                var content = new Content<(ChatId, ChatEntryCursor)>(chatIdEnvelope.Content.Id, payload);
                return Task.FromResult(new Envelope<(ChatId, ChatEntryCursor)>(content, chatIdEnvelope.CancellationToken));
            });
        var broadcastCursor = new BroadcastBlock<Envelope<(ChatId, ChatEntryCursor)>>(cursor => cursor);
        var loadNewChatEntries = new TransformBlock<Envelope<(ChatId, ChatEntryCursor)>, Envelope<IReadOnlyList<ChatEntry>>>(
            envelope => TransformAsync(envelope, newEntriesLoader));
        var loadUpdatedAndRemovedEntries =
            new TransformBlock<Envelope<(ChatId, ChatEntryCursor)>, Envelope<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)>>(
            envelope => TransformAsync(envelope, entryUpdatesLoader));
        var joinChatIdAndResults = new JoinBlock<Envelope<ChatId>, Envelope<IReadOnlyList<ChatEntry>>, Envelope<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)>>();
        var envelopeChatIdAndResults = new TransformBlock<
            Tuple<Envelope<ChatId>, Envelope<IReadOnlyList<ChatEntry>>, Envelope<(IReadOnlyList<ChatEntry>, IReadOnlyList<ChatEntry>)>>,
            Envelope<PipelineOutput>>(
            input => {
                var (chatIdEnvelope, newEntryEnvelope, updatedAndRemovedEnvelope) = (input.Item1, input.Item2, input.Item3);
                var payload = new PipelineOutput(
                    chatIdEnvelope.Content.Payload, (
                        newEntryEnvelope.Content.Payload,
                        updatedAndRemovedEnvelope.Content.Payload.Item1,
                        updatedAndRemovedEnvelope.Content.Payload.Item2
                ));
                var content = new Content<PipelineOutput>(chatIdEnvelope.Content.Id, payload);
                return Task.FromResult(new Envelope<PipelineOutput>(content, chatIdEnvelope.CancellationToken));
            });

        var sinkBlock = new TransformBlock<Envelope<PipelineOutput>, Envelope<PipelineOutput>>(
            envelope => ExecuteTransformAsync(envelope, async (pipelineOutput, cancellationToken) => {
                var inserts = Enumerable.Concat(
                    pipelineOutput.Entries.New ?? [],
                    pipelineOutput.Entries.Updated ?? []
                );
                await _sink.ExecuteAsync((inserts, pipelineOutput.Entries.Removed ?? []), cancellationToken).ConfigureAwait(false);
                return pipelineOutput;
            }));

        var saveCursorBlock = new TransformBlock<Envelope<PipelineOutput>, Envelope<IndexingResult>>(
            envelope => ExecuteTransformAsync(envelope, async (pipelineOutput, cancellationToken) => {
                var (created, updated, removed) = pipelineOutput.Entries;
                var lastTouchedEntry = created.Concat(updated).Concat(removed).MaxBy(e=>e.Version);
                if (lastTouchedEntry is null) {
                    return new IndexingResult(IsEndReached: true);
                }

                var next = new ChatEntryCursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
                await _cursorStates.ExecuteAsync((pipelineOutput.ChatId, next), cancellationToken).ConfigureAwait(false);
                return new IndexingResult(IsEndReached: true);
            }));

        _completeBlock = new(Complete);

        _inputBlock.LinkTo(_initBlock, linkOptions);
        _initBlock.LinkTo(getChatId, linkOptions);
        getChatId.LinkTo(broadcastChatId, linkOptions);
        broadcastChatId.LinkTo(loadCursor, linkOptions);
        broadcastChatId.LinkTo(joinChatIdAndCursor.Target1, linkOptions);
        broadcastChatId.LinkTo(joinChatIdAndResults.Target1, linkOptions);
        loadCursor.LinkTo(joinChatIdAndCursor.Target2, linkOptions);
        joinChatIdAndCursor.LinkTo(envelopeChatIdAndCursor, linkOptions);
        envelopeChatIdAndCursor.LinkTo(broadcastCursor, linkOptions);
        broadcastCursor.LinkTo(loadNewChatEntries, linkOptions);
        broadcastCursor.LinkTo(loadUpdatedAndRemovedEntries, linkOptions);
        loadNewChatEntries.LinkTo(joinChatIdAndResults.Target2, linkOptions);
        loadUpdatedAndRemovedEntries.LinkTo(joinChatIdAndResults.Target3, linkOptions);
        joinChatIdAndResults.LinkTo(envelopeChatIdAndResults, linkOptions);
        envelopeChatIdAndResults.LinkTo(sinkBlock, linkOptions);
        sinkBlock.LinkTo(saveCursorBlock, linkOptions);
        saveCursorBlock.LinkTo(_completeBlock, linkOptions);
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
        => ExecuteTransformAsync(envelope, transform.ExecuteAsync);

    private Task<Envelope<TInput>> SinkAsync<TInput>(Envelope<TInput> envelope, ISink<TInput> sink)
        => ExecuteTransformAsync(envelope, async (input, cancellationToken) => {
            await sink.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            return input;
        });

    private async Task<Envelope<TOutput>> ExecuteTransformAsync<TInput, TOutput>(
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

    private void Complete(Envelope<IndexingResult> envelope)
    {
        var jobId = envelope.Content.Id;
        if (_runningJobs.TryRemove(jobId, out var jobMetadata)) {
            // TODO: Log and Trace
            jobMetadata.CancellationSource.DisposeSilently();
        }
    }

    public async Task PostAsync((ChatEntryId, ChangeKind) input, CancellationToken cancellationToken)
        => await _inputBlock.SendAsync(input, cancellationToken).ConfigureAwait(false);

    public async Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken)
    {
        // Infinitely wait for cancellation while dataflow pipeline processes
        // chat index requests
        await Task.Delay(TimeSpan.MaxValue, cancellationToken).ConfigureAwait(false);

        // Complete input channel so it doesn't accept input messages anymore
        // and also it propagates completion through the pipeline.
        _inputBlock.Complete();

        await _completeBlock.Completion.ConfigureAwait(false);

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
