
using ActualChat.Chat.Events;

namespace ActualChat.MLSearch.Engine.Indexing.Spout;

internal class ChatBot(ICommander commander)
    : IChatBot, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual Task OnCommand(MLSearch_TriggerContinueConversationWithBot e, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var peerChatId = eventCommand.Entry.ChatId.PeerChatId;
        if (!peerChatId.IsNone && peerChatId.HasUser(Constants.User.MLSearchBot.UserId)) {
            // User is writing of changing something.
            var e = new MLSearch_TriggerContinueConversationWithBot(eventCommand.Entry.ChatId);
            await commander.Call(e, cancellationToken).ConfigureAwait(false);
        }
    }
}
