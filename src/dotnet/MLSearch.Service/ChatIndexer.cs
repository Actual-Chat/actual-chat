
using ActualChat.Chat.Events;

namespace ActualChat.MLSearch.Engine.Indexing.Spout;

// NOTE: solution drawback
// There is a chance that the receiving counter part will fail to complete
// event handling while the event will be marked as complete.
// This means: At most once logic.

internal class ChatIndexer(ICommander commander, IChatIndexerWorker indexerWorker)
    : IChatIndexer, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerChatIndexing e, CancellationToken cancellationToken)
        => await indexerWorker.PostAsync(new(e.Id, e.ChangeKind), cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // TODO: The question is should we index search chats as well? (Probably yes)
        var peerChatId = eventCommand.Entry.ChatId.PeerChatId;
        if (peerChatId.IsNone || !peerChatId.HasUser(Constants.User.MLSearchBot.UserId)) {
            // A normal conversation.
            var e = new MLSearch_TriggerChatIndexing(eventCommand.Entry.Id, eventCommand.ChangeKind);
            await commander.Call(e, cancellationToken).ConfigureAwait(false);
        }
    }
}
