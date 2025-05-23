using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Diagnostics;
using ActualChat.Queues;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexWorker : IWorker<MLSearch_TriggerChatIndexing>;

internal sealed class ChatContentIndexWorker(
    IChatContentUpdateLoader chatUpdateLoader,
    ICursorStates<ChatContentCursor> cursorStates,
    IChatInfoIndexer chatInfoIndexer,
    IChatContentIndexerFactory indexerFactory,
    IQueues queues
) : IChatContentIndexWorker
{
    private const string IndexChatInfoActivityName = $"IndexChatInfo@{nameof(ChatContentIndexWorker)}";
    private const string LoadCursorActivityName = $"LoadCursor@{nameof(ChatContentIndexWorker)}";
    private const string InitIndexerActivityName = $"InitIndexer@{nameof(ChatContentIndexWorker)}";
    private const string ApplyActivityName = $"Apply@{nameof(ChatContentIndexWorker)}";
    private const string FlushActivityName = $"Flush@{nameof(ChatContentIndexWorker)}";
    private const string NumOfAppliedEventsTag = "num_of_applied_events";

    private static readonly ActivitySource ActivitySource = MLSearchInstruments.ActivitySource;
    public int FlushInterval { get; init; } = 10;
    public int MaxEventCount { get; init; } = 50;

    [ActivatorUtilitiesConstructor]
    public ChatContentIndexWorker(
        int flushInterval,
        int maxEventCount,
        IChatContentUpdateLoader chatUpdateLoader,
        ICursorStates<ChatContentCursor> cursorStates,
        IChatInfoIndexer chatInfoIndexer,
        IChatContentIndexerFactory indexerFactory,
        IQueues queues
    ) : this(chatUpdateLoader, cursorStates, chatInfoIndexer, indexerFactory, queues)
    {
        FlushInterval = flushInterval;
        MaxEventCount = maxEventCount;
    }

    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.ChatId;

        // ReSharper disable once ExplicitCallerInfoArgument
        using (ActivitySource.StartActivity(IndexChatInfoActivityName))
            await chatInfoIndexer.IndexAsync(chatId, cancellationToken).ConfigureAwait(false);

        if (job.IndexingKind == IndexingKind.ChatInfo)
            return;

        var cursor = await LoadCursorAsync(chatId).ConfigureAwait(false);
        var indexer = await indexerFactory.Create(chatId).ConfigureAwait(false);

        // ReSharper disable once ExplicitCallerInfoArgument
        using (ActivitySource.StartActivity(InitIndexerActivityName))
            await indexer.InitAsync(cursor, cancellationToken).ConfigureAwait(false);

        // ReSharper disable once ExplicitCallerInfoArgument
        var applyActivity = ActivitySource.StartActivity(ApplyActivityName);
        try {
            await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
                await indexer.ApplyAsync(entry, cancellationToken).ConfigureAwait(false);
                if (++eventCount % FlushInterval == 0) {
                    applyActivity?.SetTag(NumOfAppliedEventsTag, FlushInterval);
                    applyActivity?.Dispose();
                    applyActivity = null;

                    await FlushAsync(chatId).ConfigureAwait(false);

                    // ReSharper disable once ExplicitCallerInfoArgument
                    applyActivity = ActivitySource.StartActivity(ApplyActivityName);
                }
                if (eventCount == MaxEventCount) {
                    break;
                }
            }

            applyActivity?.SetTag(NumOfAppliedEventsTag, eventCount % FlushInterval);
        }
        finally {
            applyActivity?.Dispose();
        }
        await FlushAsync(chatId).ConfigureAwait(false);

        if (eventCount == MaxEventCount) {
            await queues.Enqueue(job, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(chatId);
            await queues.Enqueue(completionNotification, cancellationToken).ConfigureAwait(false);
        }
        return;

        async Task<ChatContentCursor> LoadCursorAsync(ChatId chatId1)
        {
            // ReSharper disable once ExplicitCallerInfoArgument
            using var _ = ActivitySource.StartActivity(LoadCursorActivityName);
            return await cursorStates.LoadAsync(chatId1.Value, cancellationToken).ConfigureAwait(false) ?? new(0, 0);
        }

        async Task FlushAsync(ChatId chatId1)
        {
            // ReSharper disable once ExplicitCallerInfoArgument
            using var _ = ActivitySource.StartActivity(FlushActivityName);

            var newCursor = await indexer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await cursorStates.SaveAsync(chatId1.Value, newCursor, cancellationToken).ConfigureAwait(false);
        }
    }

    private IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatContentCursor cursor, CancellationToken cancellationToken)
        => chatUpdateLoader.LoadChatUpdatesAsync(targetId, cursor.LastEntryVersion, cursor.LastEntryLocalId, cancellationToken);
}
