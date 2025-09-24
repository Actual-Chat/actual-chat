using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;
using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Playback;

public class IosAudioPlaybackEngine(
    string playerId,
    TrackInfo trackInfo,
    IMediaSource source,
    IAudioPlayerBackend backend,
    AppUIHub hub) : IAudioPlaybackEngine
{
    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private bool _isInitialized;
    private BufferPlayerNode _node = null!;
    private FuncWorker _processWorker = null!;
    private OpusDecoder _decoder = null!;

    [field: AllowNull, MaybeNull]
    private AudioNodes Nodes => field ??= hub.Services.GetRequiredService<AudioNodes>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public async ValueTask DisposeAsync()
    {
        await _processWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        _node.DisposeSilently();
        _decoder.DisposeSilently();
    }

    public async Task Play(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Play", playerId);
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        if (!_isInitialized) {
            DebugLog?.LogInformation("#{PlayerId}.Play: initializing", playerId);
            _node = Nodes.CreateBufferNode();
            _processWorker = FuncWorker.Start(ProcessFeeder);
            _decoder = Opus.CreateDecoder();
            _isInitialized = true;
            DebugLog?.LogInformation("#{PlayerId}.Play: initialized", playerId);
        }
        DebugLog?.LogInformation("#{PlayerId}.Play: node.play()", playerId);
        _node.Play();
        DebugLog?.LogInformation("#{PlayerId}.Play: started", playerId);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        _node.Pause();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        _node.Play();
        return Task.CompletedTask;
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.End({abort})", playerId, abort);
        if (abort) {
            _node.Stop();
            await backend.OnEnded(null).ConfigureAwait(false);
        }
        else
            _ = BackgroundTask.Run(async () => {
                    await _node.Complete(cancellationToken).ConfigureAwait(false);
                    await backend.OnEnded(null).ConfigureAwait(false);
                },
                cancellationToken);
    }

    public Task Frame(MediaFrame frame, CancellationToken cancellationToken)
    {
        var data = _decoder.Decode(frame.Data);
        return _node.Feed(data, cancellationToken).AsTask();
    }

    private async Task ProcessFeeder(CancellationToken cancellationToken)
    {
        await foreach (var cPosition in _node.PlaybackState.Computed.Changes(cancellationToken)
                           .ConfigureAwait(false))
            await backend.OnPlaying(cPosition.Value.Position.TotalSeconds, cPosition.Value.IsPlaying, cPosition.Value.IsBufferLow);
    }
}
