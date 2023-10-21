using ActualChat.Media;
using ActualChat.Messaging;

namespace ActualChat.MediaPlayback;

public interface IPlaybackCommand
{ }

public sealed class PlayTrackCommand : IPlaybackCommand
{
    public static readonly PlayTrackCommand PlayNothing = new(null!, null!);
    public static readonly IMessageProcess<PlayTrackCommand> PlayNothingProcess =
        new MessageProcess<PlayTrackCommand>(
            PlayNothing,
            default,
            TaskCompletionSourceExt.New().WithResult(),
            TaskCompletionSourceExt.New<object?>().WithResult(null));

    public TrackInfo TrackInfo { get; }
    public IMediaSource Source { get; }
    public Moment PlayAt { get; init; } // rel. to CpuClock.Now
    public Symbol TrackId => TrackInfo.TrackId;

    public PlayTrackCommand(TrackInfo trackInfo, IMediaSource source)
    {
        TrackInfo = trackInfo;
        Source = source;
    }
}
