using ActualChat.Hashing;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class SendingMessages(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, ChatSendingMessages> _chatSendingMessages = new ();

    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;

    public ChatSendingMessagesAccessor GetSendingMessages(ChatId chatId, long rangeEnd)
    {
        var chatSendingMessages = GetChatSendingMessages(chatId);
        return new ChatSendingMessagesAccessor(chatSendingMessages, rangeEnd);
    }

    public async Task<ChatEntry> Post(Chats_UpsertTextEntry cmd, CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        var postResultTask = PostInternal(cmd, cancellationToken);
        var chatSendingMessages = GetChatSendingMessages(cmd.ChatId);
        var isNewMessage = cmd.LocalId is null;
        // We need to set content hash to trigger ChatEntryMessageInternalView re-rendering for edited messages.
        var textHash = isNewMessage ? HashString.None : cmd.Text.Hash().Blake2b().ToBase64HashString(HashAlgorithm.Blake2b);
        var sendingMessage = new SendingMessage(cmd.ChatId, cmd.LocalId, now, cmd.Text, textHash);
        chatSendingMessages.AddSendingMessage(sendingMessage);
        // Log.LogInformation("Sending message: LocalId={LocalId}, Content='{Content}'", cmd.LocalId, cmd.Text);
        var postResult = await postResultTask.ConfigureAwait(false);
        // TODO(DF): how to handle failures to send message?
        var chatEntry = postResult.Value;
        // Log.LogInformation("Sent message: LocalId={LocalId}, Content='{Content}'", chatEntry.LocalId, chatEntry.Content);
        if (isNewMessage)
            AnalyticEvents.RaiseMessagePosted(
                cmd.RepliedEntryLid.HasValue,
                !cmd.Text.IsNullOrEmpty(),
                cmd.EntryAttachments.Length);
        chatSendingMessages.ConfirmMessageHasSent(sendingMessage, chatEntry);
        return chatEntry;
    }

    private async Task<UIActionResult<ChatEntry>> PostInternal(Chats_UpsertTextEntry cmd, CancellationToken cancellationToken)
    {
        // Simulate long sending
        await Task.Delay(7000, cancellationToken).ConfigureAwait(false);
        // ReSharper disable once ArrangeMethodOrOperatorBody
        return await UICommander.Run(cmd, cancellationToken).ConfigureAwait(false);
    }

    private ChatSendingMessages GetChatSendingMessages(ChatId chatId)
        => _chatSendingMessages.GetOrAdd(chatId, static (chatId1, self) => new ChatSendingMessages(self, chatId1), this);
}
