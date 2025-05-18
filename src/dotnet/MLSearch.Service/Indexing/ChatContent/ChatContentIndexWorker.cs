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
    IQueues queues,
    ILogger<ChatContentIndexWorker> log
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
        IQueues queues,
        ILogger<ChatContentIndexWorker> log
    ) : this(chatUpdateLoader, cursorStates, chatInfoIndexer, indexerFactory, queues, log)
    {
        FlushInterval = flushInterval;
        MaxEventCount = maxEventCount;
    }

    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.ChatId;

        log.LogInformation("SMIDX: Begin semantic indexing of chat {}.", chatId);

        using (ActivitySource.StartActivity(IndexChatInfoActivityName, ActivityKind.Internal)) {
            await chatInfoIndexer.IndexAsync(chatId, cancellationToken).ConfigureAwait(false);
        }

        if (job.IndexingKind == IndexingKind.ChatInfo)
            return;

        var cursor = await LoadCursorAsync(chatId, cancellationToken).ConfigureAwait(false);

        var indexer = await indexerFactory.Create(chatId).ConfigureAwait(false);

        using (ActivitySource.StartActivity(InitIndexerActivityName, ActivityKind.Internal)) {
            await indexer.InitAsync(cursor, cancellationToken).ConfigureAwait(false);
        }

        var applyActivity = ActivitySource.StartActivity(ApplyActivityName, ActivityKind.Internal);
        try {
            await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
                await indexer.ApplyAsync(entry, cancellationToken).ConfigureAwait(false);
                if (++eventCount % FlushInterval == 0) {
                    await FlushAsync().ConfigureAwait(false);

                    applyActivity?
                        .SetTag(NumOfAppliedEventsTag, FlushInterval)
                        .SetStatus(ActivityStatusCode.Ok)
                        .Dispose();
                    applyActivity = ActivitySource.StartActivity(ApplyActivityName, ActivityKind.Internal);
                }
                if (eventCount == MaxEventCount) {
                    break;
                }
            }

            var remainingEventCount = eventCount % FlushInterval;
            if (remainingEventCount > 0) {
                await FlushAsync().ConfigureAwait(false);
                _ = applyActivity?
                    .SetTag(NumOfAppliedEventsTag, remainingEventCount)
                    .SetStatus(ActivityStatusCode.Ok);
            }
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            _ = applyActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally {
            applyActivity?.Dispose();
        }

        if (eventCount == MaxEventCount) {
            log.LogInformation("SMIDX: Rescheduling of semantic indexing of chat {}.", chatId);
            await queues.Enqueue(job, cancellationToken).ConfigureAwait(false);
            var continuationNotification = new MLSearch_SignalChatIndexingContinuation(chatId);
            await queues.Enqueue(continuationNotification, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            log.LogInformation("SMIDX: Semantic indexing of chat {} is completed.", chatId);
            var completionNotification = new MLSearch_SignalChatIndexingCompletion(chatId);
            await queues.Enqueue(completionNotification, cancellationToken).ConfigureAwait(false);
        }
        return;

        async Task<ChatContentCursor> LoadCursorAsync(ChatId chatId, CancellationToken cancellationToken)
        {
            using var _ = ActivitySource.StartActivity(LoadCursorActivityName, ActivityKind.Internal);
            return await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false) ?? new(0, 0);
        }

        async Task FlushAsync()
        {
            using var _ = ActivitySource.StartActivity(FlushActivityName, ActivityKind.Internal);

            var newCursor = await indexer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await cursorStates.SaveAsync(chatId, newCursor, cancellationToken).ConfigureAwait(false);
        }
    }

    private IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatContentCursor cursor, CancellationToken cancellationToken)
        => chatUpdateLoader.LoadChatUpdatesAsync(targetId, cursor.LastEntryVersion, cursor.LastEntryLocalId, cancellationToken);
}
