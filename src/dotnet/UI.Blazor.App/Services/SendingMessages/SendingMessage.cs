using ActualChat.Hashing;

namespace ActualChat.UI.Blazor.App.Services;

public record SendingMessage(ChatId ChatId, long? LocalId, Moment BeginsAt, string Content, HashString ContentHash)
{
    public ChatEntry? PostedChatEntry { get; private set; }

    public void ConfirmHasSent(ChatEntry chatEntry)
        => PostedChatEntry = chatEntry;
}
