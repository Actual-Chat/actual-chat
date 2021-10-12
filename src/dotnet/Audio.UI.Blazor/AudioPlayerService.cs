using ActualChat.Playback;
using Stl.DependencyInjection;

namespace ActualChat.Audio.UI.Blazor;

public class AudioPlayerService : MediaPlayerService
{
    public AudioPlayerService(IServiceProvider services, ILogger<MediaPlayerService> log) : base(services, log) { }

    protected override MediaTrackPlayer CreateMediaTrackPlayer(MediaTrack mediaTrack)
        => Services.Activate<AudioTrackPlayer>(mediaTrack);
}
