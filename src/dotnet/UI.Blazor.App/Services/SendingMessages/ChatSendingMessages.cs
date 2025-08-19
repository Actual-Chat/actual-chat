namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessages
{
    private readonly ChatSendingMessagesTriggers _triggers;
    private readonly Lock _lock = new ();
    private readonly List<SendingMessage> _newMessages = new ();
    private readonly List<SendingMessage> _editMessages = new ();

    public SendingMessages Owner { get; init; }
    public ChatId ChatId { get; init; }

    public bool IsEmpty {
        get {
            lock (_lock)
                return _newMessages.Count == 0 && _editMessages.Count == 0;
        }
    }

    public ChatSendingMessages(SendingMessages owner, ChatId chatId)
    {
        Owner = owner;
        ChatId = chatId;
        _triggers = owner.Hub.Services.GetRequiredService<ChatSendingMessagesTriggers>();
    }

    public async Task<SendingMessage[]> GetNewMessages()
    {
        await _triggers.OnNewMessagesChanged(ChatId).ConfigureAwait(false);
        lock (_lock)
            return _newMessages.Count > 0 ? _newMessages.Where(c => !c.ToBeRemoved).ToArray() : [];
    }

    public async Task<SendingMessage?> GetEditedMessage(ChatEntryId chatEntryId)
    {
        await _triggers.OnEditMessageChanged(chatEntryId).ConfigureAwait(false);
        lock (_lock)
            return _editMessages.Count > 0 ? _editMessages.LastOrDefault(c => !c.ToBeRemoved && c.LocalId == chatEntryId.LocalId) : null;
    }

    public void AddSendingMessage(SendingMessage sendingMessage)
    {
        lock (_lock)
            GetCollectionFor(sendingMessage).Add(sendingMessage);
        InvalidateCollection(sendingMessage);
    }

    public void ConfirmMessageHasSent(SendingMessage sendingMessage, ChatEntry chatEntry, Moment now)
    {
        lock (_lock)
            sendingMessage.Complete(chatEntry, now);

        if (sendingMessage.LocalId.HasValue)
            InvalidateCollection(sendingMessage);
        using (Invalidation.Begin())
            _ = _triggers.IsSending(sendingMessage);
    }

    public void ConfirmMessageFailedToSend(SendingMessage sendingMessage, Exception error)
    {
        sendingMessage.Complete(error);
        sendingMessage.MarkToRemove();
        InvalidateCollection(sendingMessage);
        using (Invalidation.Begin())
            _ = _triggers.IsSending(sendingMessage);
    }

    public void RemoveSentNewMessages(long rangeEnd)
    {
        lock (_lock) {
            if (_newMessages.Count == 0)
                return;

            foreach (var sendingMessage in _newMessages.Where(m => m.PostedChatEntry is not null && m.PostedChatEntry.LocalId < rangeEnd))
                sendingMessage.MarkToRemove();
        }
    }

    public void ConfirmEditedMessagedHasLoaded(SendingMessage sendingMessage)
        => sendingMessage.MarkToRemove();

    public void PruneSentMessages(Moment now)
    {
        lock (_lock) {
            var threshold = now + TimeSpan.FromMinutes(-1);
            Prune(_editMessages, threshold);
            Prune(_newMessages, threshold);
        }
        return;

        static void Prune(List<SendingMessage> messages, Moment threshold)
        {
            var toRemove = messages.FindAll(c => c.ToBeRemoved || (c.SentMoment.HasValue && c.SentMoment < threshold));
            foreach (var sendingMessage in toRemove) {
                messages.Remove(sendingMessage);
                sendingMessage.Dispose();
            }
        }
    }

    private void InvalidateCollection(SendingMessage sendingMessage)
    {
        using (Invalidation.Begin())
            if (!sendingMessage.LocalId.HasValue)
                _ = _triggers.OnNewMessagesChanged(ChatId);
            else
                _ = _triggers.OnEditMessageChanged(TextEntryId.New(ChatId, sendingMessage.LocalId.Value));
    }

    private List<SendingMessage> GetCollectionFor(SendingMessage sendingMessage)
        => sendingMessage.LocalId.HasValue ? _editMessages : _newMessages;

    public Task<bool> IsSending(SendingMessage sendingMessage)
        => _triggers.IsSending(sendingMessage);
}
