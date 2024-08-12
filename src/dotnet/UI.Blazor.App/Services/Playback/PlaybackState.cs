namespace ActualChat.UI.Blazor.App.Services;

public abstract record PlaybackState
{
    // This record relies on referential equality
    public virtual bool Equals(PlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public sealed record RealtimePlaybackState(ImmutableHashSet<ChatId> ChatIds) : PlaybackState
{
    // This record relies on referential equality
    public bool Equals(RealtimePlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

public sealed record HistoricalPlaybackState(ChatId ChatId, Moment StartAt) : PlaybackState
{
    // This record relies on referential equality
    public bool Equals(HistoricalPlaybackState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
