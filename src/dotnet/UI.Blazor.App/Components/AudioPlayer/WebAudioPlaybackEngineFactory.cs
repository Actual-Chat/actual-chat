using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.UI.Blazor.App.Components.AudioPlayer;

public sealed class WebAudioPlaybackEngineFactory(IServiceProvider services) : IAudioPlaybackEngineFactory
{
    public IAudioPlaybackEngine Create(string playerId, TrackInfo trackInfo, IMediaSource source, IAudioPlayerBackend backend)
        => new WebAudioPlaybackEngine(playerId, trackInfo, source, backend, services);
}
