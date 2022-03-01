namespace ActualChat.MediaPlayback;

/// <summary> Must be scoped service </summary>
public interface IPlaybackFactory
{
    Playback Create();
}

/// <inheritdoc cref="IPlaybackFactory"/>
public class PlaybackFactory : IPlaybackFactory
{
    private readonly IActivePlaybackInfo _activePlaybackInfo;
    private readonly IStateFactory _stateFactory;
    private readonly ITrackPlayerFactory _trackPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;

    public PlaybackFactory(
        IActivePlaybackInfo activePlaybackInfo,
        IStateFactory stateFactory,
        ITrackPlayerFactory trackPlayerFactory,
        ILoggerFactory loggerFactory
    )
    {
        _activePlaybackInfo = activePlaybackInfo;
        _stateFactory = stateFactory;
        _trackPlayerFactory = trackPlayerFactory;
        _loggerFactory = loggerFactory;
    }

    public Playback Create()
    {
        var playback = new Playback(_stateFactory, _trackPlayerFactory, _loggerFactory.CreateLogger<Playback>());
        // don't capture `this`, just in case
        var activePlaybackInfo = _activePlaybackInfo;
        playback.OnTrackPlayingChanged += (trackInfo, state) => {
            activePlaybackInfo.RegisterStateChange(trackInfo.TrackId, state);
        };
        return playback;
    }
}
