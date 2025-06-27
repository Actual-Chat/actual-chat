using ActualChat.Chat;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.Queues;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Bot;

public class ChatBotConversationTriggerOptions {
    public bool AllowPeerBotChat { get; set; }
}

internal class ChatBotConversationTrigger(
    IQueues queues,
    IChatsBackend chats,
    IWorkerPool<MLSearch_TriggerContinueConversationWithBot, ChatId, ChatId> workerPool,
    IOptionsMonitor<ChatBotConversationTriggerOptions> options
    ) : IChatBotConversationTrigger
{
    // ReSharper disable once UnusedMember.Global
    // [CommandHandler]
    public virtual async Task OnCommand(MLSearch_TriggerContinueConversationWithBot job, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        await workerPool.PostAsync(job, cancellationToken).ConfigureAwait(false);
    }

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var chat = await chats.Get(eventCommand.Entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        if (eventCommand.Entry.IsSystemEntry)
            return; // Skip system messages

        // The chat must either have a correct tag, or...
        if (!chat.IsAiSearchChat()) {
            // It must be a chat with a bot + the settings allowing that
            var allowPeerBotChat = options.CurrentValue.AllowPeerBotChat;
            if (!allowPeerBotChat)
                return;

            // Otherwise, it must be a peer chat with a bot
            if (chat.Id is not PeerChatId peerChatId)
                return;
            if (!peerChatId.HasUser(Constants.User.Sherlock.UserId))
                return;
        }
        // Something has changed in the chat with a bot
        var e = new MLSearch_TriggerContinueConversationWithBot(eventCommand.Entry.ChatId);
        await queues.Enqueue(e, cancellationToken).ConfigureAwait(false);
    }
}
