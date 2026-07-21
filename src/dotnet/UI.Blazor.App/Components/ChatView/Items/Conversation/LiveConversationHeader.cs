namespace ActualChat.UI.Blazor.App.Components;

public sealed class LiveConversationHeader : ChatMessage
{
    public LiveConversationHeader(Conversation conversation) : base(conversation.Id.StartEntryLid)
    {
        Conversation = conversation;
        ShouldSkipKey = true;
    }

    public override bool Equals(ChatMessage? other)
        => ReferenceEquals(this, other)
            || (other is LiveConversationHeader o
                && Conversation!.VersionEquals(o.Conversation)
                && Kind == o.Kind && Date == o.Date && Flags == o.Flags);

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Kind, Date, Flags);
}
