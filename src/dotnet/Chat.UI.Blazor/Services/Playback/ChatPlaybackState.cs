namespace ActualChat.Chat.UI.Blazor.Services;

public abstract record ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(ChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public record RealtimeChatPlaybackState(ImmutableHashSet<ChatId> ChatIds) : ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(RealtimeChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public record HistoricalChatPlaybackState(ChatId ChatId, Moment StartAt) : ChatPlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(HistoricalChatPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
