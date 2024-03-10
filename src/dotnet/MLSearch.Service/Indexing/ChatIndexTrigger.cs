
using ActualChat.Chat.Events;
using ActualChat.MLSearch.Indexing;

namespace ActualChat.MLSearch.Indexing;

// NOTE: solution drawback
// There is a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.

internal class ChatIndexTrigger(ICommander commander, IChatIndexerWorker indexerWorker)
    : IChatIndexTrigger, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken)
        => await indexerWorker.Trigger.WriteAsync(e, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // TODO: The question is should we index search chats as well? (Probably yes)
        var peerChatId = eventCommand.Entry.ChatId.PeerChatId;
        if (peerChatId.IsNone || !peerChatId.HasUser(Constants.User.MLSearchBot.UserId)) {
            // A normal conversation.
            var e = new MLSearch_TriggerChatIndexing(eventCommand.Entry.ChatId);
            await commander.Call(e, cancellationToken).ConfigureAwait(false);
        }
    }
}
