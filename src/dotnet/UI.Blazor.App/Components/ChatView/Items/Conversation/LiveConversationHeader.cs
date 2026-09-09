namespace ActualChat.UI.Blazor.App.Components;

public sealed class LiveConversationHeader : ChatMessage
{
    public LiveConversationHeader(Conversation conversation) : base(conversation.Id.StartEntryLid)
    {
        Conversation = conversation;
        ShouldSkipKey = true;
    }

    // Decided by the tile builder - the same build that places (or folds) the block's rows - so the
    // header can never show one state while the rows show another.
    public bool IsExpanded { get; init; }

    public override bool Equals(ChatMessage? other)
        => ReferenceEquals(this, other)
            || (other is LiveConversationHeader o
                && Conversation!.VersionEquals(o.Conversation)
                && Kind == o.Kind && Date == o.Date && Flags == o.Flags
                && IsExpanded == o.IsExpanded);

    public override int GetHashCode()
        => HashCode.Combine(Conversation, Kind, Date, Flags, IsExpanded);
}
