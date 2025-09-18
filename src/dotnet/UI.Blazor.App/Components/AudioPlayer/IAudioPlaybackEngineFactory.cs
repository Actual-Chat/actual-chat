using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IAudioPlaybackEngineFactory
{
    IAudioPlaybackEngine Create(string playerId, TrackInfo trackInfo, IMediaSource source, IAudioPlayerBackend backend);
}
