namespace ActualChat.UI.Blazor.App.Services;

public class ChatSendingMessagesAccessor(ChatSendingMessages chatSendingMessages)
{
    public ChatSendingMessages ChatSendingMessages { get; } = chatSendingMessages;
    public ChatId ChatId => ChatSendingMessages.ChatId;
    public SendingMessages Owner => ChatSendingMessages.Owner;
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Owner.Hub.LogFor<ChatSendingMessagesAccessor>();

    public async Task<ChatEntry> GetSelfOrEdited(ChatEntry chatEntry)
    {
        var e2 = await ChatSendingMessages.GetEditedMessage(chatEntry.Id).ConfigureAwait(false);
        if (e2 is null)
            return chatEntry;

        if (e2.PostedChatEntry is not null && e2.PostedChatEntry.Version <= chatEntry.Version)
            ChatSendingMessages.ConfirmEditedMessagedHasLoaded(e2);
        else {
            chatEntry = chatEntry with {
                //BeginsAt = e2.BeginsAt,
                Version = chatEntry.Version + 1,
                IsSending = true,
                Content = e2.Content,
                ContentHash = e2.ContentHash,
            };
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
            var entryId = TextEntryId.New(ChatId, localId);
            var chatEntry = new ChatEntry(entryId, 0) {
                AuthorId = ownAuthorId,
                Content = sendingMessage.Content,
                BeginsAt = sendingMessage.BeginsAt,
                IsSending = true,
            };
            entries.Add(chatEntry);
            localId++;
        }
        return entries.ToArray();
    }

    public void RemoveSentNewMessages(long rangeEnd)
        => ChatSendingMessages.RemoveSentNewMessages(rangeEnd);
}
