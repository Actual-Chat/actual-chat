namespace ActualChat.MediaPlayback;

public sealed record PlayerState
{
    public TimeSpan PlayingAt { get; init; }
    public bool IsStarted { get; init; }
    public bool IsPaused { get; init; }
    /// <summary> Returns <see langword="true" /> if the playback was stopped OR ended. </summary>
    public bool IsCompleted { get; init; }
    public Exception? Error { get; init; }

    // TODO: do we need this?
    // This record relies on referential equality
    public bool Equals(PlayerState? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
