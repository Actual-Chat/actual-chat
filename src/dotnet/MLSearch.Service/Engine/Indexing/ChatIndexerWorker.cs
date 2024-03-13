using System.Threading.Tasks.Dataflow;
using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters;
using ActualChat.MLSearch.DataFlow;

using TPipelineOutput = (
    System.Collections.Generic.IEnumerable<ActualChat.Chat.ChatEntry>?,
    System.Collections.Generic.IEnumerable<ActualChat.Chat.ChatEntry>?
);

namespace ActualChat.MLSearch.Engine.Indexing;

internal record ChatIndexerInput(ChatEntryId ChatEntryId, ChangeKind ChangeKind);

internal interface IChatIndexerWorker: ISpout<ChatIndexerInput>
{
    Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken);
}

internal class ChatIndexerWorker : IChatIndexerWorker
{
    // [StructLayout(LayoutKind.Auto)]
    // private readonly struct Content<TPayload>
    // {
    //     public static Content<TPayload> None = new();
    //     public readonly ChatId Id { get; }
    //     private readonly TPayload? _payload;
    //     public readonly TPayload Payload
    //         => _hasPayload ? _payload! : throw new InvalidOperationException("Container is empty.");
    //     private readonly bool _hasPayload;
    //     public readonly bool IsEmpty => !_hasPayload;
    //     public Content(ChatId id) => (Id, _payload, _hasPayload) = (id, default, false);
    //     public Content(ChatId id, TPayload payload) => (Id, _payload, _hasPayload) = (id, payload, true);
    // };
    // private record class Envelope<TPayload>(Content<TPayload> Content, CancellationToken CancellationToken);
    // private record class PipelineOutput(ChatId ChatId, (IReadOnlyList<ChatEntry> New, IReadOnlyList<ChatEntry> Updated, IReadOnlyList<ChatEntry> Removed) Entries);

    private class Context
    {
        public static readonly Context Empty = new();
        // We assume concurrent access only to the Error property and PostError method
        private readonly object _errorSyncRoot = new();
        private readonly ChatIndexerInput? _input;
        private readonly CancellationToken? _cancellationToken;
        private Exception? _error;
        private ChatEntryCursor? _cursor;
        private IReadOnlyList<ChatEntry>? _newEntries;
        private IReadOnlyList<ChatEntry>? _updatedEntries;
        private IReadOnlyList<ChatEntry>? _deletedEntries;
        private ChatEntryCursor? _newCursor;
        private IndexingResult? _indexingResult;

        private Context() {}
        public Context(ChatIndexerInput input, CancellationToken cancellationToken)
            => (_input, _cancellationToken) = (input, cancellationToken);

        public bool IsEmpty => !_cancellationToken.HasValue;
        public bool IsFaulted => Error is not null;
        public Exception? Error => ReadNotEmpty(() => {
            lock (_errorSyncRoot) {
                return _error;
            }
        });

        public ChatId JobId => ReadNotEmpty(() => _input!.ChatEntryId.ChatId);
        public CancellationToken CancellationToken => ReadNotEmpty(() => _cancellationToken!.Value);
        public ChatId ChatId => ReadNotEmpty(() => _input!.ChatEntryId.ChatId);
        public ChatEntryCursor Cursor {
            get => ReadNotEmpty(() => _cursor ?? throw NotReadyException(nameof(Cursor)));
            set {
                AssertNotEmpty();
                _cursor = value;
            }
        }
        public IReadOnlyList<ChatEntry> NewEntries {
            get => ReadNotEmpty(() => _newEntries ?? throw NotReadyException(nameof(NewEntries)));
            set {
                AssertNotEmpty();
                _newEntries = value;
            }
        }
        public IReadOnlyList<ChatEntry> UpdatedEntries {
            get => ReadNotEmpty(() => _updatedEntries ?? throw NotReadyException(nameof(UpdatedEntries)));
            set {
                AssertNotEmpty();
                _updatedEntries = value;
            }
        }
        public IReadOnlyList<ChatEntry> DeletedEntries {
            get => ReadNotEmpty(() => _deletedEntries ?? throw NotReadyException(nameof(DeletedEntries)));
            set {
                AssertNotEmpty();
                _deletedEntries = value;
            }
        }
        public ChatEntryCursor NewCursor {
            get => ReadNotEmpty(() => _newCursor ?? throw NotReadyException(nameof(NewCursor)));
            set {
                AssertNotEmpty();
                _newCursor = value;
            }
        }
        public IndexingResult IndexingResult {
            get => ReadNotEmpty(() => _indexingResult ?? throw NotReadyException(nameof(IndexingResult)));
            set {
                AssertNotEmpty();
                _indexingResult = value;
            }
        }

        private void AssertNotEmpty()
        {
            if (IsEmpty) {
                throw new InvalidOperationException("Can't operate on empty context.");
            }
        }
        private TValue ReadNotEmpty<TValue>(Func<TValue> accessor) {
            AssertNotEmpty();
            return accessor();
        }
        private static Exception NotReadyException(string propertyName)
            => new InvalidOperationException($"{propertyName} is not ready yet.");

        public void PostError(Exception exception) {
            lock(_errorSyncRoot) {
                if (_error is null) {
                    _error = exception;
                }
                else if (_error is AggregateException aggregate) {
                    _error = new AggregateException(aggregate.InnerExceptions.Append(exception));
                }
                else {
                    _error = new AggregateException(_error, exception);
                }
            }
        }
        public void Clear()
        {
            if (IsFaulted) {
                throw new InvalidOperationException("Context is faulted.");
            }
            _cursor = default;
            _newEntries = default;
            _updatedEntries = default;
            _deletedEntries = default;
            _newCursor = default;
            _indexingResult = default;
        }
    }

    private record class JobMetadata(CancellationTokenSource CancellationSource);
    private const int ChannelCapacity = 10;
    private readonly IChatEntryCursorStates _cursorStates;
    private readonly ISink<TPipelineOutput> _sink;
    private readonly ILoggerSource _loggerSource;
    private readonly BufferBlock<Context> _inputBlock;
    private readonly TransformBlock<ChatIndexerInput, Context> _initBlock;
    private readonly ActionBlock<Context> _completeBlock;
    private readonly ConcurrentDictionary<ChatId, JobMetadata> _runningJobs;
    private readonly SemaphoreSlim _semaphore;
    private ILogger? _log;
    private ILogger Log => _log ??= _loggerSource.GetLogger(GetType());

    public ChatIndexerWorker(
        IChatEntryCursorStates cursorStates,
        INewChatEntryLoader newEntriesLoader,
        IUpdatedAndRemovedEntryLoader entryUpdatesLoader,
        ISink<(IEnumerable<ChatEntry>?, IEnumerable<ChatEntry>?)> sink,
        ILoggerSource loggerSource
    )
    {
        _cursorStates = cursorStates;
        _sink = sink;
        _loggerSource = loggerSource;

        _runningJobs = new();
        _semaphore = new(0, ChannelCapacity);

        _initBlock = new(Init);
        _inputBlock = new(); // Unbounded buffer
        var loadCursor = new TransformBlock<Context, Context>(
            context => ExecuteOperationAsync(context, async (context, cancellationToken) => {
                context.Cursor = await _cursorStates.ExecuteAsync(context.ChatId, cancellationToken).ConfigureAwait(false);
            }));
        var broadcastContext = new BroadcastBlock<Context>(context => context);
        var loadNewChatEntries = new TransformBlock<Context, Context>(
            context => ExecuteOperationAsync(context, async (context, cancellationToken) => {
                context.NewEntries = await newEntriesLoader
                    .ExecuteAsync((context.ChatId, context.Cursor), cancellationToken)
                    .ConfigureAwait(false);
            }));
        var loadUpdatedAndRemovedEntries = new TransformBlock<Context, Context>(
            context => ExecuteOperationAsync(context, async (context, cancellationToken) => {
                var (updatedEntries, deletedEntries) = await entryUpdatesLoader
                    .ExecuteAsync((context.ChatId, context.Cursor), cancellationToken)
                    .ConfigureAwait(false);
                context.UpdatedEntries = updatedEntries;
                context.DeletedEntries = deletedEntries;
            }));
        var waitResults = new JoinBlock<Context, Context>();
        var mergeContext = new TransformBlock<Tuple<Context, Context>, Context>(input => input.Item1);

        var sinkBlock = new TransformBlock<Context, Context>(
            context => ExecuteOperationAsync(context, async (context, cancellationToken) => {
                var inserts = context.NewEntries.Concat(context.UpdatedEntries);
                await _sink.ExecuteAsync((inserts, context.DeletedEntries), cancellationToken).ConfigureAwait(false);
            }));

        var saveCursorBlock = new TransformBlock<Context, Context>(
            context => ExecuteOperationAsync(context, async (context, cancellationToken) => {
                var lastTouchedEntry = context.NewEntries
                    .Concat(context.UpdatedEntries)
                    .Concat(context.DeletedEntries)
                    .MaxBy(e=>e.Version);
                if (lastTouchedEntry is null) {
                    context.IndexingResult = new IndexingResult(IsEndReached: true);
                }
                else {
                    var next = new ChatEntryCursor(lastTouchedEntry.LocalId, lastTouchedEntry.Version);
                    await _cursorStates.ExecuteAsync((context.ChatId, next), cancellationToken).ConfigureAwait(false);
                    context.NewCursor = next;
                    context.IndexingResult = new IndexingResult(IsEndReached: false);
                }
            }));

        _completeBlock = new(CompleteAsync);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        _initBlock.LinkTo(_inputBlock, linkOptions);
        _inputBlock.LinkTo(loadCursor, linkOptions);
        loadCursor.LinkTo(broadcastContext, linkOptions);
        broadcastContext.LinkTo(loadNewChatEntries, linkOptions);
        broadcastContext.LinkTo(loadUpdatedAndRemovedEntries, linkOptions);
        loadNewChatEntries.LinkTo(waitResults.Target1, linkOptions);
        loadUpdatedAndRemovedEntries.LinkTo(waitResults.Target2, linkOptions);
        waitResults.LinkTo(mergeContext, linkOptions);
        mergeContext.LinkTo(sinkBlock, linkOptions);
        sinkBlock.LinkTo(saveCursorBlock, linkOptions);
        saveCursorBlock.LinkTo(_completeBlock, linkOptions);
    }

    private Context Init(ChatIndexerInput input)
    {
        var cancellationSource = new CancellationTokenSource();
        var context = new Context(input, cancellationSource.Token);
        if (_runningJobs.TryAdd(context.JobId, new(cancellationSource))) {
            return context;
        }
        // We do not start indexing of the same chat in parallel
        cancellationSource.DisposeSilently();
        return Context.Empty;
    }

    private async Task<Context> ExecuteOperationAsync(
        Context context, Func<Context,CancellationToken,Task> updateContext)
    {
        if (!context.IsEmpty && !context.IsFaulted && !context.CancellationToken.IsCancellationRequested) {
            try {
                await updateContext(context, context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                Log.LogInformation($"Operation {context.JobId} is cancelled.");
            }
            catch (Exception e) {
                Log.LogError(e, $"Operation {context.JobId} is failed.");
                context.PostError(e);
            }
        }
        return context;
    }

    private async Task CompleteAsync(Context context)
    {
        if (context.IsEmpty) {
            return;
        }
        // TODO: Log and Trace
        if (!context.IsFaulted && !context.IndexingResult.IsEndReached) {
            context.Clear();
            await _inputBlock.SendAsync(context).ConfigureAwait(false);
        }
        else {
            if (_runningJobs.TryRemove(context.JobId, out var jobMetadata)) {
                jobMetadata.CancellationSource.DisposeSilently();
            }
            _semaphore.Release();
        }
    }

    public async Task PostAsync(ChatIndexerInput input, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        await _initBlock.SendAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(int shardIndex, CancellationToken cancellationToken)
    {
        // Infinitely wait for cancellation while dataflow pipeline processes
        // chat index requests
        await Task.Delay(TimeSpan.MaxValue, cancellationToken).ConfigureAwait(false);

        // Complete input channel so it doesn't accept input messages anymore
        // and also it propagates completion through the pipeline.
        _inputBlock.Complete();

        await _completeBlock.Completion.ConfigureAwait(false);
    }

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
