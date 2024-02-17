namespace ActualChat.MediaPlayback;

public sealed record PlayerState
{
    public TimeSpan PlayingAt { get; init; }
    public bool IsStarted { get; init; }
    public bool IsPaused { get; init; }
    public bool IsEnded { get; init; }
    public Exception? Error { get; init; }

    // This record relies on referential equality
    public bool Equals(PlayerState? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
