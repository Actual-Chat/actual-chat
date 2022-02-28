using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IPlayerCommand { }
public interface IPlaybackCommand { }

public sealed class PlayCommand : IPlayerCommand
{
    public static readonly PlayCommand Instance = new();
    private PlayCommand() { }
}

/// <summary> Playing is cancelled by user action or an exception. </summary>
public sealed class StopCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly StopCommand Instance = new();
    private StopCommand() { }
}

/// <summary> Occurs when EOF is reached. </summary>
public sealed class EndCommand : IPlayerCommand
{
    public static readonly EndCommand Instance = new();
    private EndCommand() { }
}

/// <summary>
/// When <see cref="Playback"/> get this, it should create player
/// (using <seealso cref="ITrackPlayerFactory"/>) and send <seealso cref="PlayCommand"/> to it.
/// <para>This record relies on referential equality. </para>
/// We use it as a key in dictionaries and reference-based comparison works faster there.
/// </summary>
public sealed record PlayTrackCommand(TrackInfo TrackInfo, IMediaSource Source) : IPlaybackCommand
{
    public Symbol TrackId => TrackInfo.TrackId;
    public Moment PlayAt { get; init; } // rel. to CpuClock.Now
    public bool Equals(PlayTrackCommand? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}