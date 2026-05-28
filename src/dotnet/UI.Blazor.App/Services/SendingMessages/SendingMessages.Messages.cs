using ActualChat.Hashing;

namespace ActualChat.UI.Blazor.App.Services;

partial class SendingMessages
{
    public ChatSendingMessagesAccessor GetSendingMessages(ChatId chatId)
    {
        var chatSendingMessages = GetChatSendingMessages(chatId);
        DebugLog?.LogDebug("-> GetSendingMessages. ChatId='{ChatId}'", chatId);
        return new ChatSendingMessagesAccessor(chatSendingMessages);
    }

    public void NotifyAttachmentsLoaded(ChatEntryId chatEntryId)
        => _mediaUploadsUI.NotifyAttachmentsLoaded(chatEntryId);

    public AttachmentUploads? GetMediaUploads(ChatEntry entry)
        => _mediaUploadsUI.GetMediaUploads(entry);

    public void RegisterEntryByClientId(ChatEntry chatEntry)
    {
        lock (_clientEntries.SyncObject)
            _clientEntries.AddOrUpdate(chatEntry.ClientUid, chatEntry);
    }

    public ChatEntry? TryGetChatEntryByClientId(string clientId)
    {
        lock (_clientEntries.SyncObject)
            return _clientEntries.TryGetValue(clientId, out var chatEntry) ? chatEntry : null;
    }

    public void Cancel(SendingMessage sendingMessage)
        => sendingMessage.Cancel();

    private SendingMessage CreateAndRegisterSendingMessage(
        PostMessageRequestInternal request,
        Action cancelSendRequested)
    {
        var sendingMessage = CreateSendingMessage(request, cancelSendRequested);
        lock (_chatSendingMessagesLock) {
            var chatSendingMessages = GetChatSendingMessages(request.ChatId);
            chatSendingMessages.AddSendingMessage(sendingMessage);
            if (request.AttachmentUploads is not null)
                _mediaUploadsUI.Add(sendingMessage, request.AttachmentUploads);
        }
        return sendingMessage;
    }

    private static SendingMessage CreateSendingMessage(
        PostMessageRequestInternal request,
        Action cancelSendRequested)
    {
        var isNewMessage = request.LocalId is null;
        // NOTE(DF): we need to set the content hash to trigger ChatEntryMessageInternalView re-rendering for edited messages.
        var textHash = isNewMessage ? HashString.None : request.Text.Hash().Blake2b().ToBase64HashString(HashAlgorithm.Blake2b);
        var sendingMessage = new SendingMessage(request.Uuid,
            request.ChatId,
            request.LocalId,
            request.Now,
            request.Text,
            textHash,
            request.AttachmentUploads,
            cancelSendRequested);
        return sendingMessage;
    }

    private ChatSendingMessages GetChatSendingMessages(ChatId chatId)
    {
        lock (_chatSendingMessagesLock) {
            if (_chatSendingMessages.TryGetValue(chatId, out var chatSendingMessages))
                return chatSendingMessages;

            chatSendingMessages = new ChatSendingMessages(this, _triggers, chatId);
            _chatSendingMessages.Add(chatId, chatSendingMessages);
            return chatSendingMessages;
        }
    }

    private Task PruneSendingMessages(CancellationToken cancellationToken)
    {
        lock (_chatSendingMessagesLock) {
            var keys = _chatSendingMessages.Keys.ToArray();
            foreach (var chatId in keys) {
                if (!_chatSendingMessages.TryGetValue(chatId, out var chatSendingMessages))
                    continue;

                chatSendingMessages.PruneSentMessages(Now);
                // NOTE(DF): do not remove empty, they will be recreated on GetSendingMessages again.
                // if (chatSendingMessages.IsEmpty) {
                //     _chatSendingMessages.Remove(chatId);
                //     DebugLog?.LogInformation("Removed ChatSendingMessages for chat '{ChatId}'", chatId);
                // }
            }
        }
        return Task.CompletedTask;
    }
}
