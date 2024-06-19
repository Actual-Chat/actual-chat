
using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;

namespace ActualChat.MLSearch.Bot;

internal class ChatBotConversationTrigger(ICommander commander, IChatsBackend chats, IWorkerPool<MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId> workerPool)
    : IChatBotConversationTrigger, IComputeService
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerContinueConversationWithBot job, CancellationToken cancellationToken)
        => await workerPool.PostAsync(job, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var chat = await chats.Get(eventCommand.Entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null) {
            return;
        }
        if (eventCommand.Entry.IsSystemEntry) {
            return;
        }
        if (!OrdinalEquals(Constants.Chat.SystemTags.Bot, chat.SystemTag)) {
            return;
        }
        /*
        if (eventCommand.Entry.AuthorId == Constants.User.MLSearchBot.UserId) {
            return;
        }
        */
        
        // User is writing of changing something.
        var e = new MLSearch_TriggerContinueConversationWithBot(eventCommand.Entry.ChatId);
        await commander.Call(e, cancellationToken).ConfigureAwait(false);
    
    }
}
