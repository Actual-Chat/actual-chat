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
    private readonly ILogger<Playback> _playbackLog;
    private readonly Action<TrackInfo, PlayerState> _onTrackPlayingChanged;

    public PlaybackFactory(
        IStateFactory stateFactory,
        ITrackPlayerFactory trackPlayerFactory,
        IActivePlaybackInfo activePlaybackInfo,
        ILogger<Playback> playbackLog)
    {
        _stateFactory = stateFactory;
        _trackPlayerFactory = trackPlayerFactory;
        _activePlaybackInfo = activePlaybackInfo;
        _playbackLog = playbackLog;
        _onTrackPlayingChanged = OnTrackPlayingChanged;
    }

    public Playback Create()
    {
        var playback = new Playback(_stateFactory, _trackPlayerFactory, _playbackLog);
        playback.OnTrackPlayingChanged += _onTrackPlayingChanged;
        return playback;
    }

    private void OnTrackPlayingChanged(TrackInfo trackInfo, PlayerState state)
        => _activePlaybackInfo.RegisterStateChange(trackInfo, state);
}
