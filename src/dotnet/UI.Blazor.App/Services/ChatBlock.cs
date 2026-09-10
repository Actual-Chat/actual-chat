namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatBlock(
    Conversation Conversation,
    Range<long> EntryLidRange,
    Moment? CollapsedAt)
{
    public ConversationId Id => Conversation.Id;
    public bool IsExpanded => CollapsedAt == null;
}
