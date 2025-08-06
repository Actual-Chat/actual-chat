using ActualChat.Hashing;

namespace ActualChat.UI.Blazor.App.Services;

public record SendingMessage(ChatId ChatId, long? LocalId, Moment BeginsAt, string Content, HashString ContentHash)
{
    public ChatEntry? PostedChatEntry { get; private set; }
    public Moment? SentMoment { get; private set; }
    public bool ToBeRemoved { get; private set; }

    public void ConfirmHasSent(ChatEntry chatEntry, Moment now)
    {
        PostedChatEntry = chatEntry;
        SentMoment = now;
    }

    public void MarkToRemove()
        => ToBeRemoved = true;
}
