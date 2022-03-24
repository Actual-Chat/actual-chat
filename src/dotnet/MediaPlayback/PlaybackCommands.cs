using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IPlaybackCommand
{
    /// <summary>
    /// The token passed to <see cref="Playback.Play"/> call.
    /// </summary>
    CancellationToken CancellationToken { get; }
}

public sealed class PlayTrackCommand
    : IPlaybackCommand
{
    public TrackInfo TrackInfo { get; }
    public IMediaSource Source { get; }
    public CancellationToken CancellationToken { get; }
    public Moment PlayAt { get; init; } // rel. to CpuClock.Now
    public Symbol TrackId => TrackInfo.TrackId;

    public PlayTrackCommand(TrackInfo trackInfo, IMediaSource source, CancellationToken cancellationToken)
    {
        TrackInfo = trackInfo;
        Source = source;
        CancellationToken = cancellationToken;
    }
}

public sealed class PlayNothingCommand : IPlaybackCommand
{
    public CancellationToken CancellationToken => CancellationToken.None;
}

