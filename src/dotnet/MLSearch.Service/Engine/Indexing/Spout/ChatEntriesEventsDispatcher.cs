using ActualChat.Chat.Events;

namespace ActualChat.MLSearch.Engine.Indexing.Spout;

internal interface IChatEntriesEventsDispatcher
{
    Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

internal class ChatEntriesEventsDispatcher (ICommander commander) : IComputeService, IChatEntriesEventsDispatcher
{
    // ReSharper disable once UnusedMember.Global
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        // TODO: Decide if we want to have it in the dispatcher vs indexing services all have their own filters.
        if (!eventCommand.Entry.ChatId.PeerChatId.IsNone
            && eventCommand.Entry.ChatId.PeerChatId.HasUser(Constants.User.MLSearchBot.UserId)
        ) {
            // A conversation with ML Search bot
            await OnMLSearchBotConversation(eventCommand, cancellationToken).ConfigureAwait(false);
        } else {
            // A normal conversation.
            var e = new MLSearch_TriggerContinueChatIndexing(eventCommand.Entry.ChatId);
            await commander.Call(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task OnMLSearchBotConversation(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (eventCommand.Author.UserId != ActualChat.Constants.User.MLSearchBot.UserId) {
            // User is writing of changing something.
            var e = new MLSearch_TriggerContinueConversationWithBot(eventCommand.Entry.ChatId);
            await commander.Call(e, cancellationToken).ConfigureAwait(false);
        }
        return;
    }
}
