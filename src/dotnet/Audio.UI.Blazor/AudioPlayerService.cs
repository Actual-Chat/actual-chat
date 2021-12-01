using ActualChat.Playback;
using Stl.DependencyInjection;

namespace ActualChat.Audio.UI.Blazor;

public class AudioPlayerService : MediaPlayerService
{
    public AudioPlayerService(IServiceProvider services, ILogger<AudioPlayerService> log)
        : base(services, log) { }

    protected override MediaTrackPlayer CreateMediaTrackPlayer(
        MediaPlaybackState playbackState,
        PlayMediaTrackCommand playTrackCommand)
        => Services.Activate<AudioTrackPlayer>(playbackState, playTrackCommand);
}
