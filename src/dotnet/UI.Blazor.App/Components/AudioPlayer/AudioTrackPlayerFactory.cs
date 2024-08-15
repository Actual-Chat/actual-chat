using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioTrackPlayerFactory(IServiceProvider services) : ITrackPlayerFactory
{
    private ulong _lastCreatedId;

    public TrackPlayer Create(IMediaSource source) => new AudioTrackPlayer(
        Interlocked.Increment(ref _lastCreatedId).Format(),
        source,
        services);
}
