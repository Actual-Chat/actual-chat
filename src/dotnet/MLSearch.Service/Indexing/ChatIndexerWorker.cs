using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerWorker: IWorker<MLSearch_TriggerChatIndexing>;

internal sealed class ChatIndexerWorker(
    IChatsBackend chats,
    IChatEntryCursorStates cursorStates,
    IChatIndexerFactory indexerFactory,
    ICommander commander
) : IChatIndexerWorker
{
    private const int BatchSize = 100;
    private const int MaxEventCount = 5000;

    public async Task ExecuteAsync(MLSearch_TriggerChatIndexing job, CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var chatId = job.Id;

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false);

        var indexer = indexerFactory.Create();
        await indexer.InitAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
            var eventType = entry.IsRemoved ? ChatEventType.Remove
                : entry.LocalId > cursor.LastEntryLocalId ? ChatEventType.New : ChatEventType.Update;
            await indexer.ApplyAsync(new ChatEvent(eventType, entry), cancellationToken).ConfigureAwait(false);
            if (++eventCount % BatchSize == 0) {
                cursor = await FlushAsync().ConfigureAwait(false);
            }
            if (eventCount==MaxEventCount) {
                break;
            }
        }

        _ = await FlushAsync().ConfigureAwait(false);

        if (eventCount==MaxEventCount) {
            await commander.Call(job, cancellationToken).ConfigureAwait(false);
        }
        else if (!cancellationToken.IsCancellationRequested) {
            var completionNotification = new MLSearch_TriggerChatIndexingCompletion(job.Id);
            await commander.Call(completionNotification, cancellationToken).ConfigureAwait(false);
        }

        async Task<ChatEntryCursor> FlushAsync()
        {
            var newCursor = await indexer.FlushAsync(cancellationToken).ConfigureAwait(false);
            await cursorStates.SaveAsync(chatId, newCursor, cancellationToken).ConfigureAwait(false);
            return newCursor;
        }
    }

    private async IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatEntryCursor cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bool continueProcessing;
        var (lastEntryVersion, lastEntryLocalId) = (cursor.LastEntryVersion, cursor.LastEntryLocalId);
        do {
            var entries = await chats
                .ListChangedEntries2(targetId, lastEntryVersion, lastEntryLocalId, BatchSize, cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in entries) {
                lastEntryVersion = entry.Version;
                lastEntryLocalId = entry.LocalId;
                yield return entry;
            }
            continueProcessing = entries.Count == BatchSize;
        }
        while (continueProcessing);
    }
}

internal interface IChatIndexerFactory
{
    IChatIndexer Create();
}

internal enum ChatEventType
{
    New,
    Update,
    Remove
}

internal record ChatEvent(ChatEventType Type, ChatEntry ChatEntry) : IHasId<ChatEntryCursor>
{
    public ChatEntryCursor Id => new(ChatEntry.Version, ChatEntry.LocalId);
}

internal interface IChatIndexer
{
    Task InitAsync(CancellationToken cancellationToken);
    ValueTask ApplyAsync(ChatEvent @event, CancellationToken cancellationToken);
    Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken);
}
