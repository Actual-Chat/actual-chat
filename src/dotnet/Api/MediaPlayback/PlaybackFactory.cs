using ActualChat.Hardware;

namespace ActualChat.MediaPlayback;

/// <summary> Must be scoped service </summary>
public interface IPlaybackFactory
{
    Playback Create();
}

public class PlaybackFactory : IPlaybackFactory
{
    private readonly ActivePlaybackInfo _activePlaybackInfo;
    private readonly StateFactory _stateFactory;
    private readonly ITrackPlayerFactory _trackPlayerFactory;
    private readonly ISleepDurationProvider _sleepDurationProvider;
    private readonly ILogger<Playback> _playbackLog;
    private readonly Action<TrackInfo, PlayerState> _onTrackPlayingChanged;

    public PlaybackFactory(IServiceProvider services)
    {
        _stateFactory = services.StateFactory();
        _trackPlayerFactory = services.GetRequiredService<ITrackPlayerFactory>();
        _sleepDurationProvider = services.GetRequiredService<ISleepDurationProvider>();
        _activePlaybackInfo = services.GetRequiredService<ActivePlaybackInfo>();
        _playbackLog = services.LogFor<Playback>();
        _onTrackPlayingChanged = OnTrackPlayingChanged;
    }

    public Playback Create()
    {
        var playback = new Playback(_stateFactory, _trackPlayerFactory, _sleepDurationProvider, _playbackLog);
        playback.OnTrackPlayingChanged += _onTrackPlayingChanged;
        return playback;
    }

    private void OnTrackPlayingChanged(TrackInfo trackInfo, PlayerState state)
        => _activePlaybackInfo.RegisterStateChange(trackInfo, state);
}
