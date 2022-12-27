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

    public PlaybackFactory(IServiceProvider services)
    {
        _stateFactory = services.GetRequiredService<IStateFactory>();
        _trackPlayerFactory = services.GetRequiredService<ITrackPlayerFactory>();
        _activePlaybackInfo = services.GetRequiredService<IActivePlaybackInfo>();
        _playbackLog = services.GetRequiredService<ILogger<Playback>>();
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
