using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;
using ActualLab.Opus.MaciOS;
using ActualLab.Pooling;

namespace ActualChat.App.Maui.Audio;

public class IosAudioPlaybackEngine(
    string playerId,
    TrackInfo trackInfo,
    IMediaSource source,
    IAudioPlayerBackend backend,
    AppUIHub hub) : IAudioPlaybackEngine
{
    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private bool _isInitialized;
    private BufferedPlayer _bufferedPlayer = null!;
    private FuncWorker _processFeederWorker = null!;
    private OpusDecoder _decoder = null!;
    private IResourceLease<AudioEngine> _engineLease = null!;

    [field: AllowNull, MaybeNull]
    private AudioEngines AudioEngines => field ??= hub.Services.GetRequiredService<AudioEngines>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public async ValueTask DisposeAsync()
    {
        await _processFeederWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        _bufferedPlayer.DisposeSilently();
        _decoder.DisposeSilently();
        _engineLease.DisposeSilently();
    }

    public async Task Play(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Play", playerId);
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        if (!_isInitialized) {
            DebugLog?.LogInformation("#{PlayerId}.Play: initializing", playerId);
            // TODO (FC): cleanup
            _engineLease = await AudioEngines.Rent(AudioMode.Playback).ConfigureAwait(false);
            _bufferedPlayer = new BufferedPlayer(playerId, _engineLease.Resource, hub);
            _processFeederWorker = FuncWorker.Start(MonitorPlayer);
            _decoder = Opus.CreateDecoder();
            _isInitialized = true;
            DebugLog?.LogInformation("#{PlayerId}.Play: initialized", playerId);
        }
        _engineLease.Resource.EnsureRunning();
        DebugLog?.LogInformation("#{PlayerId}.Play: node.play()", playerId);
        _bufferedPlayer.Play();
        DebugLog?.LogInformation("#{PlayerId}.Play: started", playerId);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        _bufferedPlayer.Pause();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        _bufferedPlayer.Play();
        return Task.CompletedTask;
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.End({abort})", playerId, abort);
        if (abort) {
            _bufferedPlayer.Abort();
            await backend.OnEnded(null).ConfigureAwait(false);
        }
        else
            _ = BackgroundTask.Run(async () => {
                    await _bufferedPlayer.Complete(cancellationToken).ConfigureAwait(false);
                    await backend.OnEnded(null).ConfigureAwait(false);
                },
                cancellationToken);
    }

    public ValueTask PushFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        var data = _decoder.Decode(frame.Data);
        return _bufferedPlayer.Feed(data, cancellationToken);
    }

    private async Task MonitorPlayer(CancellationToken cancellationToken)
    {
        await foreach (var cPosition in _bufferedPlayer.PlaybackState.Computed.Changes(cancellationToken)
                           .ConfigureAwait(false))
            await backend.OnPlaying(cPosition.Value.Position.TotalSeconds, cPosition.Value.IsPlaying, cPosition.Value.IsBufferLow);
    }
}
