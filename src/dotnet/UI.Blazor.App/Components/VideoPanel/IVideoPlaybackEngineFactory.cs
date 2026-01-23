using ActualChat.Media;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.MediaPlayback;

public interface IVideoPlaybackEngineFactory
{
    IVideoPlaybackEngine Create(
        string playerId,
        VideoStreamInfo streamInfo,
        IMediaSource source,
        IVideoPlayerBackend backend);
}
