namespace ActualChat.UI.Blazor.App.Components;

public sealed class LiveConversationFooter : ChatMessage
{
    public LiveConversationFooter(Conversation conversation) : base(conversation.EndEntryLid)
    {
        Conversation = conversation;
        ShouldSkipKey = true;
    }

    public override bool Equals(ChatMessage? other)
        => ReferenceEquals(this, other)
            || (other is LiveConversationFooter o
                && Conversation!.VersionEquals(o.Conversation)
                && Kind == o.Kind && Date == o.Date && Flags == o.Flags);

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Kind, Date, Flags);
}
