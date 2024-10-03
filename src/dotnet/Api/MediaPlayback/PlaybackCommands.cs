using ActualChat.Media;
using ActualChat.Messaging;

namespace ActualChat.MediaPlayback;

public interface IPlaybackCommand
{ }

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
    public Moment PlayAt { get; init; } // rel. to CpuClock.Now
    public Symbol TrackId => TrackInfo.TrackId;
}
