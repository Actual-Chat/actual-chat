using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public abstract record PlaybackCommand
{
    public Task<Unit> WhenProcessed { get; init; } = TaskSource.New<Unit>(true).Task;
}

public sealed record SetVolumeCommand(double Volume) : PlaybackCommand
{
    // This record relies on referential equality
    public bool Equals(SetVolumeCommand? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}

public sealed record StopCommand() : PlaybackCommand
{
    // This record relies on referential equality
    public bool Equals(StopCommand? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}

public sealed record PlayTrackCommand(
    TrackInfo TrackInfo,
    IMediaSource Source
) : PlaybackCommand
{
    public Symbol TrackId => TrackInfo.TrackId;
    public Moment PlayAt { get; init; } // rel. to CpuClock.Now

    // This record relies on referential equality
    public bool Equals(PlayTrackCommand? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
