using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Indexing;
using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.Bot;

internal interface IChatBotWorker: IWorker<MLSearch_TriggerContinueConversationWithBot>;

internal sealed class ChatBotWorker(
    ICursorStates<ChatCursor> cursorStates,
    IChatUpdateLoader chatUpdateLoader,
    IBotConversationHandler sink
) : IChatBotWorker
{
    public async Task ExecuteAsync(MLSearch_TriggerContinueConversationWithBot job, CancellationToken cancellationToken)
    {
        var chatId = job.Id;

        var cursor = await cursorStates.LoadAsync(chatId, cancellationToken).ConfigureAwait(false) ?? new (0, 0);
        var nextCursor = cursor;

        var updatedEntries = new List<ChatEntry>();
        var deletedEntries = new List<ChatEntryId>();
        await foreach (var entry in GetUpdatedEntriesAsync(chatId, cursor, cancellationToken).ConfigureAwait(false)) {
            if (entry.IsRemoved) {
                deletedEntries.Add(entry.Id);
            }
            else {
                updatedEntries.Add(entry);
            }
            if (new ChatCursor(entry) is var entryCursor && entryCursor > nextCursor) {
                nextCursor = entryCursor;
            }
        }

        await sink.ExecuteAsync(updatedEntries, deletedEntries, cancellationToken).ConfigureAwait(false);
        await cursorStates.SaveAsync(chatId, nextCursor, cancellationToken).ConfigureAwait(false);
    }

    private IAsyncEnumerable<ChatEntry> GetUpdatedEntriesAsync(
        ChatId targetId, ChatCursor cursor, CancellationToken cancellationToken)
        => chatUpdateLoader.LoadChatUpdatesAsync(targetId, cursor, cancellationToken);
}
