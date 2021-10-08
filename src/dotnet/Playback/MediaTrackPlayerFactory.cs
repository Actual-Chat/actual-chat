namespace ActualChat.Playback;

public class MediaTrackPlayerFactory : IMediaTrackPlayerFactory
{
    private readonly IReadOnlyList<Func<MediaTrack, MediaTrackPlayer?>> _factories;

    public MediaTrackPlayerFactory()
    {
        _factories = new List<Func<MediaTrack, MediaTrackPlayer?>>();
    }

    private MediaTrackPlayerFactory(IReadOnlyList<Func<MediaTrack, MediaTrackPlayer?>> factories)
    {
        _factories = factories;
    }

    public MediaTrackPlayerFactory WithFactory(Func<MediaTrack, MediaTrackPlayer?> factory)
    {
        var list = new List<Func<MediaTrack, MediaTrackPlayer?>>(_factories) { factory };
        return new MediaTrackPlayerFactory(list);
    }

    public MediaTrackPlayer CreatePlayer(MediaTrack track)
    {
        foreach (var factory in _factories) {
            var player = factory(track);
            if (player != null) {
                return player;
            }
        }

        throw new InvalidOperationException($"There is no factory registered for the {track.GetType()} player");
    }
}
