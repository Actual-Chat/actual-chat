namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatBlock(
    Conversation Conversation,
    Range<long> EntryLidRange,
    bool IsExpanded,
    bool IsLive)
{
    public ConversationId Id => Conversation.Id;
    // Compatibility defaults only: collapse actions do not record a cutoff yet.
    public Moment CollapsedAt { get; init; } = IsLive
        ? Conversation.StartsAt
        : Conversation.EndsAt == Moment.MaxValue ? Moment.MaxValue : Conversation.EndsAt + TimeSpan.FromTicks(1);
}
