
using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Indexing;

// NOTE: solution drawback
// There is a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.

internal class ChatIndexTrigger(ICommander commander, IChatsBackend chats, IWorkerPool<MLSearch_TriggerChatIndexing, ChatId, ChatId> workerPool)
    : IChatIndexTrigger, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken)
        => await workerPool.PostAsync(e, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCancelCommand(MLSearch_CancelChatIndexing e, CancellationToken cancellationToken)
        => await workerPool.CancelAsync(e, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // Note: It should not index search chats. 
        // Reason: it finds the question itself as the best match for a query
        var peerChatId = eventCommand.Entry.ChatId.PeerChatId;
        if (!peerChatId.IsNone) {
            // Skip 1:1 conversations
            return;
        }
        var chat = await chats.Get(eventCommand.Entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null) {
            // No chat info. Skip for safety.
            return;
        }
        if (OrdinalEquals(Constants.Chat.SystemTags.Bot, chat.SystemTag)) {
            // Do not index conversations with the bot.
            return;
        }
        if (!chat.IsPublic) {
            // Do not index private chats. 
            return;
        }

        // A normal conversation.
        var e = new MLSearch_TriggerChatIndexing(eventCommand.Entry.ChatId);
        await commander.Call(e, cancellationToken).ConfigureAwait(false);
    }
}
