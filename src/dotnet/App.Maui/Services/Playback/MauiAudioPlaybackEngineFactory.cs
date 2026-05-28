using ActualChat.App.Maui.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services.Playback;

public sealed class MauiAudioPlaybackEngineFactory(AppUIHub hub) : IAudioPlaybackEngineFactory
{
    private IServiceProvider Services => hub.Services;
    public IAudioPlaybackEngine Create(string playerId, TrackInfo trackInfo, IMediaSource source, IAudioPlayerBackend backend)
    {
#if WINDOWS
        return new WindowsAudioPlaybackEngine(playerId,
            trackInfo,
            source,
            backend,
            Services);
#elif ANDROID
        return new AndroidAudioPlaybackEngine(playerId, trackInfo, source, backend, Services);
#elif IOS || MACCATALYST
        return new AppleAudioPlaybackEngine(playerId, trackInfo, backend, hub);
#else
        return new WebAudioPlaybackEngine(playerId,
            trackInfo,
            source,
            backend,
            Services);
#endif
    }
}
