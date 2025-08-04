namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessages
{
    private readonly ChatSendingMessagesTriggers _triggers;
    private readonly Lock _lock = new ();
    private readonly List<SendingMessage> _newMessages = new ();
    private readonly List<SendingMessage> _editMessages = new ();

    public SendingMessages Owner { get; init; }
    public ChatId ChatId { get; init; }

    public ChatSendingMessages(SendingMessages owner, ChatId chatId)
    {
        Owner = owner;
        ChatId = chatId;
        _triggers = owner.Hub.Services.GetRequiredService<ChatSendingMessagesTriggers>();
    }

    public void AddSendingMessage(SendingMessage sendingMessage)
    {
        lock (_lock)
            GetCollectionFor(sendingMessage).Add(sendingMessage);
        InvalidateCollection(sendingMessage);
    }

    public void ConfirmMessageFailedToSend(SendingMessage sendingMessage)
    {
        lock (_lock)
            GetCollectionFor(sendingMessage).Remove(sendingMessage);
        InvalidateCollection(sendingMessage);
    }

    public void ConfirmMessageHasSent(SendingMessage sendingMessage, ChatEntry chatEntry)
    {
        lock (_lock)
            sendingMessage.ConfirmHasSent(chatEntry);

        if (sendingMessage.LocalId.HasValue) {
            using (Invalidation.Begin())
                _ = _triggers.OnEditMessageChanged(TextEntryId.New(ChatId, sendingMessage.LocalId.Value));
        }

        _ = BackgroundTask.Run(async () => {
            // TODO(DF): improve cleanup
            await Task.Delay(60000).ConfigureAwait(false);
            lock (_lock)
                GetCollectionFor(sendingMessage).Remove(sendingMessage);
        });
    }

    private List<SendingMessage> GetCollectionFor(SendingMessage sendingMessage)
        => sendingMessage.LocalId.HasValue ? _editMessages : _newMessages;

    public async Task<SendingMessage[]> GetNewMessages()
    {
        await _triggers.OnNewMessagesChanged().ConfigureAwait(false);
        lock (_lock)
            return _newMessages.Count == 0 ? Array.Empty<SendingMessage>() : _newMessages.ToArray();
    }

    public void RemoveSentNewMessages(long rangeEnd)
    {
        lock (_lock) {
            if (_newMessages.Count == 0)
                return;

            _newMessages.RemoveAll(m => m.PostedChatEntry is not null && m.PostedChatEntry.LocalId < rangeEnd);
        }
    }

    public async Task<SendingMessage?> GetEditedMessage(ChatEntryId chatEntryId)
    {
        await _triggers.OnEditMessageChanged(chatEntryId).ConfigureAwait(false);
        lock (_lock) {
            if (_editMessages.Count == 0)
                return null;

            return _editMessages.FirstOrDefault(c => c.LocalId == chatEntryId.LocalId);
        }
    }

    public void ConfirmEditedMessagedHasLoaded(SendingMessage sendingMessage)
    {
        lock (_lock)
            _editMessages.Remove(sendingMessage);
    }

    private void InvalidateCollection(SendingMessage sendingMessage)
    {
        if (!sendingMessage.LocalId.HasValue) {
            using (Invalidation.Begin())
                _ = _triggers.OnNewMessagesChanged();
        }
        else {
            using (Invalidation.Begin())
                _ = _triggers.OnEditMessageChanged(TextEntryId.New(ChatId, sendingMessage.LocalId.Value));
        }
    }
}
