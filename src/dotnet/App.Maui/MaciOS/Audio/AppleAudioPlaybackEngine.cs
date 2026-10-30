using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleAudioPlaybackEngine(
    string playerId,
    TrackInfo info,
    IMediaSource source,
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
    private Task? _decodeFeedTask;
    private int _endedReported;
    private long _nextLagReportAtTicks;

    private IAudioCodec AudioCodec => field ??= hub.Services.GetRequiredService<IAudioCodec>();
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayer);

    public async ValueTask DisposeAsync()
    {
        _decodeFeedCts.CancelAndDisposeSilently();
        if (_decodeFeedTask is { } decodeFeedTask)
            await decodeFeedTask.SilentAwait();
        await _processFeederWorker.DisposeSilentlyAsync().ConfigureAwait(false);
        _voicePlayer.DisposeSilently();
        _decodeFeedTask = null;
    }

    public async Task Play(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Play", playerId);
        _frames.SetTargetDuration(GetEncodedBufferDuration(info.TargetBufferSize));

        _voicePlayer = new VoicePlayer(playerId, hub);
        _processFeederWorker = FuncWorker.Start(MonitorPlayer);
        _decodeFeedTask = BackgroundTask.Run(
            () => DecodeAndFeed(_decodeFeedCts.Token),
            Log,
            "Failed to decode/feed iOS audio",
            _decodeFeedCts.Token);
        await _voicePlayer.Play(cancellationToken).ConfigureAwait(false);
        // After Play(), not before: starting the player is what builds the engine and moves the route.
        await hub.AudioFocusUI.EnsureOutputRoute(cancellationToken).ConfigureAwait(false);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Pause", playerId);
        _voicePlayer.Pause();
        return Task.CompletedTask;
    }

    public async Task Resume(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{PlayerId}.Resume", playerId);
        await _voicePlayer.Play(cancellationToken).ConfigureAwait(false);
        // Same reason as Play(): resuming restarts the engine, and a restart that lands after
        // voice processing came up needs the route restated.
        await hub.AudioFocusUI.EnsureOutputRoute(cancellationToken).ConfigureAwait(false);
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
        _voicePlayer.AddBuffered();
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
            var remainingPreSkip = ((AudioSource)source).Format.PreSkip;
            var opusPackets = _frames.ReadAllAsync(cancellationToken);
            await foreach (var pcmOwner in AudioCodec.Decode(opusPackets, cancellationToken).ConfigureAwait(false)) {
                AVAudioPcmBuffer pcm;
                using (pcmOwner) {
                    var samples = pcmOwner.Memory.Span;
                    var skip = Math.Min(remainingPreSkip, samples.Length);
                    remainingPreSkip -= skip;
                    if (skip == samples.Length)
                        continue;

                    pcm = samples[skip..].ToPcmBuffer(AudioEngine.VoicePlaybackFormat);
                }
                await _voicePlayer.Feed(pcm, cancellationToken).ConfigureAwait(false);
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
