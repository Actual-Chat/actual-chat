namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessagesAccessor(ChatSendingMessages chatSendingMessages)
{
    public ChatSendingMessages ChatSendingMessages { get; } = chatSendingMessages;
    public ChatId ChatId => ChatSendingMessages.ChatId;
    public SendingMessages Owner => ChatSendingMessages.Owner;
    private ILogger Log => field ??= Owner.Hub.LogFor<ChatSendingMessagesAccessor>();

    public async Task<ChatEntry> GetSelfOrSending(ChatEntry chatEntry)
    {
        if (chatEntry.HasUploadingAttachments) {
            var newMessages = await ChatSendingMessages.GetNewMessages().ConfigureAwait(false);
            var sendingMessage = newMessages.FirstOrDefault(c => c.PostedChatEntry?.LocalId == chatEntry.LocalId);
            if (sendingMessage is not null) {
                chatEntry = chatEntry with {
                    Version = chatEntry.Version + 1,
                    SendingTag = sendingMessage,
                    ClientUid = Guid.NewGuid().ToString(),
                };
                Owner.RegisterEntryByClientId(chatEntry);
                return chatEntry;
            }
        }
        return await GetSelfOrEdited(chatEntry).ConfigureAwait(false);
    }

    public async Task<ChatEntry> GetSelfOrEdited(ChatEntry chatEntry)
    {
        var sendingMessage = await ChatSendingMessages.GetEditedMessage(chatEntry.Id).ConfigureAwait(false);
        if (sendingMessage is null)
            return chatEntry;

        if (sendingMessage.PostedChatEntry is not null && sendingMessage.PostedChatEntry.Version <= chatEntry.Version)
            ChatSendingMessages.ConfirmEditedMessageWasLoaded(sendingMessage);
        else {
            chatEntry = chatEntry with {
                //BeginsAt = e2.BeginsAt,
                Version = chatEntry.Version + 1,
                SendingTag = sendingMessage,
                Content = sendingMessage.Content,
                ContentHash = sendingMessage.ContentHash,
                ClientUid = Guid.NewGuid().ToString(),
            };
            Owner.RegisterEntryByClientId(chatEntry);
            // Log.LogInformation("Edited message: {Text}", chatEntry.Content);
        }
        return chatEntry;
    }

    public async Task<ChatEntry[]> GetNewMessages(AuthorId ownAuthorId, long rangeEnd)
    {
        var newMessages = await ChatSendingMessages.GetNewMessages().ConfigureAwait(false);
        // Log.LogInformation("Found {Count} new messages: {Messages}", newMessages.Length,
        //     newMessages.Select(c => "'" + c.Content + "' (" + c.PostedChatEntry?.LocalId + ")").ToCommaPhrase());
        if (newMessages.Length == 0)
            return Array.Empty<ChatEntry>();

        var entries = new List<ChatEntry>();
        var localId = rangeEnd;
        foreach (var sendingMessage in newMessages) {
            if (sendingMessage.LoadedForDisplay)
                continue;

            var entryId = ChatEntryId.New(ChatId, localId);
            var chatEntry = new TextEntry(entryId, 0) {
                AuthorId = ownAuthorId,
                Content = sendingMessage.Content,
                BeginsAt = sendingMessage.BeginsAt,
                SendingTag = sendingMessage,
                ClientUid = Guid.NewGuid().ToString(),
                HasUploadingAttachments = sendingMessage.AttachmentUploads is not null,
            };
            Owner.RegisterEntryByClientId(chatEntry);
            entries.Add(chatEntry);
            localId++;
        }
        return entries.ToArray();
    }

    public void ProcessLoadedEntriesRange(long rangeEnd)
        => ChatSendingMessages.ProcessLoadedEntriesRange(rangeEnd);

    public Task<bool> IsSending(SendingMessage sendingMessage)
        => ChatSendingMessages.IsSending(sendingMessage);
}
