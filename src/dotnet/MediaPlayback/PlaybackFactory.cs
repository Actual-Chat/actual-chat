using Microsoft.Extensions.Hosting;

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
    private readonly IHostApplicationLifetime _lifetime;

    public PlaybackFactory(
        IActivePlaybackInfo activePlaybackInfo,
        IStateFactory stateFactory,
        ITrackPlayerFactory trackPlayerFactory,
        ILoggerFactory loggerFactory,
        IHostApplicationLifetime lifetime)
    {
        _activePlaybackInfo = activePlaybackInfo;
        _stateFactory = stateFactory;
        _trackPlayerFactory = trackPlayerFactory;
        _loggerFactory = loggerFactory;
        _lifetime = lifetime;
    }

    public Playback Create()
    {
        var playback = new Playback(_lifetime, _stateFactory, _trackPlayerFactory, _loggerFactory.CreateLogger<Playback>());
        // don't capture `this`, just in case
        var activePlaybackInfo = _activePlaybackInfo;
        playback.OnTrackPlayingChanged += (trackInfo, state) => {
            activePlaybackInfo.RegisterStateChange(trackInfo, state);
        };
        return playback;
    }
}
