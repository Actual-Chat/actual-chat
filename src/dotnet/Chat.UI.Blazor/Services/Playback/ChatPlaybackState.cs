namespace ActualChat.Chat.UI.Blazor.Services;

public abstract record ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(ChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public record RealtimeChatPlaybackState(
    ImmutableHashSet<Symbol> ChatIds,
    bool IsPlayingPinned)
    : ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(RealtimeChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public record HistoricalChatPlaybackState(
    Symbol ChatId,
    Moment StartAt,
    RealtimeChatPlaybackState? PreviousState)
    : ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(HistoricalChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
