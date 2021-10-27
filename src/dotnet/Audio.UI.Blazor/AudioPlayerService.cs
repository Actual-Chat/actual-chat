using ActualChat.Playback;
using Stl.DependencyInjection;

namespace ActualChat.Audio.UI.Blazor;

public class AudioPlayerService : MediaPlayerService
{
    public AudioPlayerService(IServiceProvider services, ILogger<AudioPlayerService> log)
        : base(services, log) { }

    protected override MediaTrackPlayer CreateMediaTrackPlayer(PlayMediaTrackCommand mediaTrack)
        => Services.Activate<AudioTrackPlayer>(mediaTrack);
}
