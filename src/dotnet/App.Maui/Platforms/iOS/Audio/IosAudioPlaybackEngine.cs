using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Audio;

public class IosAudioPlaybackEngine(
    string playerId,
    IAudioPlayerBackend backend,
    AppUIHub hub) : IAudioPlaybackEngine
{
    private VoicePlayer _voicePlayer = null!;
    private FuncWorker _processFeederWorker = null!;
    private OpusDecoder _decoder = null!;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public async ValueTask DisposeAsync()
    {
        await _processFeederWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        _voicePlayer.DisposeSilently();
        _decoder.DisposeSilently();
    }

    public Task Play(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Play", playerId);
        _voicePlayer = new VoicePlayer(playerId, hub);
        _processFeederWorker = FuncWorker.Start(MonitorPlayer);
        _decoder = Opus.CreateDecoder();
        _voicePlayer.Play();
        return Task.CompletedTask;
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Pause", playerId);
        _voicePlayer.Pause();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Resume", playerId);
        _voicePlayer.Play();
        return Task.CompletedTask;
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.End({abort})", playerId, abort);
        if (abort) {
            _voicePlayer.Abort();
            await backend.OnEnded(null).ConfigureAwait(false);
        }
        else
            _ = BackgroundTask.Run(async () => {
                    await _voicePlayer.Complete(cancellationToken).ConfigureAwait(false);
                    await backend.OnEnded(null).ConfigureAwait(false);
                },
                cancellationToken);
    }

    public ValueTask PushFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        DebugLog?.LogTrace("#{PlayerId}.PushFrame", playerId);
        var data = _decoder.Decode(frame.Data);
        return _voicePlayer.Feed(data, cancellationToken);
    }

    private async Task MonitorPlayer(CancellationToken cancellationToken)
    {
        await foreach (var cPosition in _voicePlayer.PlaybackState.Computed.Changes(cancellationToken)
                           .ConfigureAwait(false))
            await backend.OnPlaying(cPosition.Value.Position.TotalSeconds,
                    !cPosition.Value.IsPlaying,
                    cPosition.Value.IsBufferLow)
                .ConfigureAwait(false);
    }
}
