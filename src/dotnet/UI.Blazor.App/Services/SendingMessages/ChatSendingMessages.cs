namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessages
{
    private readonly ChatSendingMessagesTriggers _triggers;
    private readonly Lock _lock = new ();
    private readonly List<SendingMessage> _newMessages = new ();
    private readonly List<SendingMessage> _editMessages = new ();

    public SendingMessages Owner { get; }
    public ChatId ChatId { get; }

    public bool IsEmpty {
        get {
            lock (_lock)
                return _newMessages.Count == 0 && _editMessages.Count == 0;
        }
    }

    public ChatSendingMessages(SendingMessages owner, ChatSendingMessagesTriggers triggers, ChatId chatId)
    {
        Owner = owner;
        ChatId = chatId;
        _triggers = triggers;
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

    public void ConfirmMessageWasSent(SendingMessage sendingMessage, ChatEntry chatEntry, Moment now, bool complete)
    {
        lock (_lock) {
            sendingMessage.Posted(chatEntry, now);
            if (complete)
                sendingMessage.Complete(now);
        }
        OnMessageSent(sendingMessage);
    }

    public void ConfirmMessageAttachmentsWereSent(SendingMessage sendingMessage, ChatEntry? chatEntry, Moment now)
    {
        lock (_lock) {
            if (chatEntry is not null)
                sendingMessage.Posted(chatEntry, now);
            sendingMessage.Complete(now);
        }
        OnMessageSent(sendingMessage);
    }

    public void ConfirmMessageFailedToSend(SendingMessage sendingMessage, Exception error)
    {
        sendingMessage.Complete(error);
        sendingMessage.MarkToRemove();
        InvalidateCollection(sendingMessage);
        using (Invalidation.Begin())
            _ = _triggers.IsSending(sendingMessage);
    }

    public void ProcessLoadedEntriesRange(long rangeEnd)
    {
        lock (_lock) {
            if (_newMessages.Count == 0)
                return;

            foreach (var sendingMessage in _newMessages.Where(m => m.PostedChatEntry is not null && m.PostedChatEntry.LocalId < rangeEnd)) {
                if (sendingMessage.IsCompleted)
                    sendingMessage.MarkToRemove();
                else
                    sendingMessage.MarkLoadedForDisplay();
            }
        }
    }

    public void ConfirmEditedMessageWasLoaded(SendingMessage sendingMessage)
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
            var toRemove = messages.FindAll(c => c.ToBeRemoved || (c.CompleteMoment.HasValue && c.CompleteMoment < threshold));
            if (toRemove.Count == 0)
                return;

            foreach (var sendingMessage in toRemove)
                messages.Remove(sendingMessage);
        }
    }

    private void InvalidateCollection(SendingMessage sendingMessage)
    {
        using (Invalidation.Begin())
            if (!sendingMessage.LocalId.HasValue)
                _ = _triggers.OnNewMessagesChanged(ChatId);
            else
                _ = _triggers.OnEditMessageChanged(ChatEntryId.New(ChatId, sendingMessage.LocalId.Value));
    }

    private List<SendingMessage> GetCollectionFor(SendingMessage sendingMessage)
        => sendingMessage.LocalId.HasValue ? _editMessages : _newMessages;

    public Task<bool> IsSending(SendingMessage sendingMessage)
        => _triggers.IsSending(sendingMessage);

    private void OnMessageSent(SendingMessage sendingMessage)
    {
        // New messages must invalidate the collection too: the tail tile drops the optimistic copy only
        // when ProcessLoadedEntriesRange sees PostedChatEntry, which is set right here - so if the real
        // entry rendered first, nothing else would ever recompute that tile and both rows would stick.
        InvalidateCollection(sendingMessage);
        using (Invalidation.Begin())
            _ = _triggers.IsSending(sendingMessage);
    }
}
