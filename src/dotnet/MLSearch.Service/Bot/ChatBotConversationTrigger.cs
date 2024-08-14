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
        => await workerPool.PostAsync(job, cancellationToken).ConfigureAwait(false);

    // ReSharper disable once UnusedMember.Global
    // [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var chat = await chats.Get(eventCommand.Entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return;

        if (eventCommand.Entry.IsSystemEntry)
            // Skip system messages.
            return;
        // The chat must have either have a correct tag
        if (!OrdinalEquals(Constants.Chat.SystemTags.Bot, chat.SystemTag)) {
            // Or it must be 1-on-1 chat with the bot with the setting set to allow that.
            var allowPeerBotChat = options.CurrentValue.AllowPeerBotChat;
            if (!allowPeerBotChat)
                // Ensure settings
                return;
            if (!chat.Id.IsPeerChat(out var peerChatId))
                // Ensure it's 1-on-1 chat
                return;
            if (!peerChatId.HasUser(Constants.User.MLSearchBot.UserId))
                // Ensure it's a chat with the bot.
                return;
        }
        // Something has changed in the chat with an ml bot.
        var e = new MLSearch_TriggerContinueConversationWithBot(eventCommand.Entry.ChatId);
        await queues.Enqueue(e, cancellationToken).ConfigureAwait(false);
    }
}
