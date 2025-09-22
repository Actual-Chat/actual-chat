using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.App.Maui.Services.Playback;

public sealed class MauiAudioPlaybackEngineFactory(IServiceProvider services) : IAudioPlaybackEngineFactory
{
    public IAudioPlaybackEngine Create(string playerId, TrackInfo trackInfo, IMediaSource source, IAudioPlayerBackend backend)
    {
#if WINDOWS
        return new ActualChat.App.Maui.Playback.WindowsAudioPlaybackEngine(playerId,
            trackInfo,
            source,
            backend,
            services);
#elif ANDROID
        return new ActualChat.App.Maui.Playback.AndroidAudioPlaybackEngine(playerId, trackInfo, source, backend, services);
        // #elif IOS
        //     return new iOSAudioPlaybackEngine(playerId, trackInfo, source, backend, services);
#else
        return new WebAudioPlaybackEngine(playerId,
            trackInfo,
            source,
            backend,
            services);
#endif
    }
}
