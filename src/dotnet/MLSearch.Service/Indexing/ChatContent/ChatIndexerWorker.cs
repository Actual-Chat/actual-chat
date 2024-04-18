using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatIndexerWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal sealed class ChatIndexerWorker(
    int flushInterval,
    int maxEventCount,
    IChatUpdateLoader chatUpdateLoader,
    IChatCursorStates cursorStates,
    IChatIndexerFactory indexerFactory,
    ICommander commander
) : IChatIndexerWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.Id;

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false);

        var indexer = indexerFactory.Create();
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
        ChatId targetId, ChatCursor cursor, CancellationToken cancellationToken)
        => chatUpdateLoader.LoadChatUpdatesAsync(targetId, cursor, cancellationToken);
}
