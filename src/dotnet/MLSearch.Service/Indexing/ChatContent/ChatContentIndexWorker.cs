using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatContentIndexWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal sealed class ChatContentIndexWorker(
    int flushInterval,
    int maxEventCount,
    IChatContentUpdateLoader chatUpdateLoader,
    ICursorStates<ChatContentCursor> cursorStates,
    IChatInfoIndexer chatInfoIndexer,
    IChatContentIndexerFactory indexerFactory,
    ICommander commander
) : IChatContentIndexWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.Id;

        await chatInfoIndexer.IndexAsync(chatId, cancellationToken).ConfigureAwait(false);

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false) ?? new(0, 0);

        var indexer = indexerFactory.Create(chatId);
        await indexer.InitAsync(cursor, cancellationToken).ConfigureAwait(false);

        await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
            await indexer.ApplyAsync(entry, cancellationToken).ConfigureAwait(false);
            if (++eventCount % flushInterval == 0) {
                await FlushAsync().ConfigureAwait(false);
            }
            if (eventCount==maxEventCount) {
                break;
            }
        }

        await FlushAsync().ConfigureAwait(false);

        if (eventCount==maxEventCount) {
            await commander.Call(job, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(job.Id);
            await commander.Call(completionNotification, cancellationToken).ConfigureAwait(false);
        }
        return;

        async Task FlushAsync()
        {
            var newCursor = await indexer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await cursorStates.SaveAsync(chatId, newCursor, cancellationToken).ConfigureAwait(false);
        }
    }

    private IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatContentCursor cursor, CancellationToken cancellationToken)
        => chatUpdateLoader.LoadChatUpdatesAsync(targetId, cursor, cancellationToken);
}
