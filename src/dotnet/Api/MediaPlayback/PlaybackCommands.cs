using ActualChat.Messaging;

namespace ActualChat.MediaPlayback;

/// <summary>
/// Marker interface for playback control commands.
/// </summary>
public interface IPlaybackCommand
{ }

/// <summary>
/// Command to start playing a specific track.
/// </summary>
public sealed class PlayTrackCommand(TrackInfo trackInfo, IMediaSource source) : IPlaybackCommand
{
    public static readonly PlayTrackCommand PlayNothing = new(null!, null!);
    public static readonly IMessageProcess<PlayTrackCommand> PlayNothingProcess =
        new MessageProcess<PlayTrackCommand>(
            PlayNothing,
            default,
            TaskCompletionSourceExt.New().WithResult(),
            TaskCompletionSourceExt.New<object?>().WithResult(null));

    public TrackInfo TrackInfo { get; } = trackInfo;
    public IMediaSource Source { get; } = source;
    public Symbol TrackId => TrackInfo.TrackId;
}
