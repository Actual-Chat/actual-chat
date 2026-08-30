using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Opus.MaciOS;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleAudioPlaybackEngine(
    string playerId,
    TrackInfo info,
    IAudioPlayerBackend backend,
    AppUIHub hub
    ) : IAudioPlaybackEngine
{
    private const long LagReportIntervalMs = 500;

    private readonly DurationTargetingFrameBuffer<AudioFrame> _frames = new(
        static frame => frame.Offset,
        static frame => frame.Duration);
    private readonly CancellationTokenSource _decodeFeedCts = new();

    private VoicePlayer _voicePlayer = null!;
    private FuncWorker _processFeederWorker = null!;
    private OpusDecoder _decoder = null!;
    private Task? _decodeFeedTask;
    private int _endedReported;
    private long _nextLagReportAtTicks;

    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayer);

    public async ValueTask DisposeAsync()
    {
        _decodeFeedCts.CancelAndDisposeSilently();
        await _decodeFeedTask.SilentAwait();
        await _processFeederWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        _voicePlayer.DisposeSilently();
        _decoder.DisposeSilently();
        _decodeFeedTask = null;
    }

    public async Task Play(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Play", playerId);
        _frames.SetTargetDuration(GetEncodedBufferDuration(info.TargetBufferSize));

        _voicePlayer = new VoicePlayer(playerId, hub);
        _processFeederWorker = FuncWorker.Start(MonitorPlayer);
        _decoder = Opus.CreateDecoder();
        _decodeFeedTask = BackgroundTask.Run(
            () => DecodeAndFeed(_decodeFeedCts.Token),
            Log,
            "Failed to decode/feed iOS audio",
            _decodeFeedCts.Token);
        _voicePlayer.Play();
        // After Play(), not before: starting the player is what builds the engine and moves the route.
        await hub.AudioFocusUI.EnsureOutputRoute(cancellationToken).ConfigureAwait(false);
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
        // Same reason as Play(): resuming restarts the engine, and a restart that lands after
        // voice processing came up needs the route restated.
        return hub.AudioFocusUI.EnsureOutputRoute(cancellationToken);
    }

    public Task End(bool abort, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.End({abort})", playerId, abort);
        if (abort) {
            _frames.Complete();
            _decodeFeedCts.CancelAndDisposeSilently();
            _voicePlayer.Abort();
            TryReportEnded(null);
        }
        else
            _frames.Complete();

        return Task.CompletedTask;
    }

    public ValueTask PushFrame(AudioFrame frame, CancellationToken cancellationToken)
    {
        DebugLog?.LogTrace("#{PlayerId}.PushFrame", playerId);
        if (frame.Data.Length == 0)
            return ValueTask.CompletedTask;

        _frames.Push(frame);
        return ValueTask.CompletedTask;
    }

    private async Task MonitorPlayer(CancellationToken cancellationToken)
    {
        await foreach (var cPosition in _voicePlayer.PlaybackState.Computed.Changes(cancellationToken).ConfigureAwait(false)) {
            var state = cPosition.Value;
            backend.OnPlaying(state.Position.TotalSeconds, !state.IsPlaying, state.IsBufferLow);
            if (state.IsPlaying && state.Position > TimeSpan.Zero) {
                // Rendering progress, not frames handed in: TrackPlayer keeps pushing into a dead
                // engine, so a push-based heartbeat would tell the owner watchdog the session is
                // fine in precisely the case it exists to rescue.
                AudioSession.NotifyPlaybackActivity();
                TryReportPresentationLag(state.Position);
            }
        }
    }

    private void TryReportPresentationLag(TimeSpan playheadOffset)
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks < Interlocked.Read(ref _nextLagReportAtTicks))
            return;

        Interlocked.Exchange(ref _nextLagReportAtTicks, nowTicks + LagReportIntervalMs);

        var anchor = info.SourceRecordedAt != default ? info.SourceRecordedAt : info.RecordedAt;
        if (anchor == default)
            return;

        var lag = hub.Clocks.ServerClock.Now - anchor - playheadOffset
            + Constants.Audio.AudioEnginePlaybackLatency;
        try {
            backend.OnPresentationLag(lag);
        }
        catch {
            // Don't propagate reporting errors
        }
    }

    private async Task DecodeAndFeed(CancellationToken cancellationToken)
    {
        try {
            await foreach (var frame in _frames.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                var data = _decoder.Decode(frame.Data.ToArray());
                await _voicePlayer.Feed(data, cancellationToken).ConfigureAwait(false);
            }
            await _voicePlayer.Complete(cancellationToken).ConfigureAwait(false);
            TryReportEnded(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected on abort/dispose
        }
        catch (Exception e) {
            Log.LogError(e, "Decode/feed loop failed");
            TryReportEnded(e.Message);
        }
    }

    private static TimeSpan GetEncodedBufferDuration(TimeSpan targetBufferSize)
    {
        var encoded = targetBufferSize - Constants.Audio.DecodedBufferSize - Constants.Audio.AudioEnginePlaybackLatency;
        return TimeSpanExt.Max(encoded, Constants.Audio.MinEncodedBufferSize);
    }

    private void TryReportEnded(string? message)
    {
        if (Interlocked.Exchange(ref _endedReported, 1) != 0)
            return;

        backend.OnEnded(message);
    }
}
