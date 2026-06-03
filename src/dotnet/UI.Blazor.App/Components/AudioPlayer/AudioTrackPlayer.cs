using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    // Pacing: push the first PacingHeadStartDuration of media instantly so JS
    // has a small backlog to start with, then pace the remaining frames at
    // real-time (until cumulative media reaches PacingDuration). Buffer-based
    // flow control takes over after that.
    private static readonly TimeSpan PacingHeadStartDuration = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan PacingDuration = TimeSpan.FromMilliseconds(200);
    private const int AudioSyncPolicySamplePeriodFrames = 10;

    private static bool DebugMode => Constants.DebugMode.AudioTrackPlayer;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private readonly string _id;
    private IAudioPlaybackEngine? _playbackEngine;
    private volatile TaskCompletionSource _whenBufferLowSource = TaskCompletionSourceExt.New();
    private CpuTimestamp _playStartedAt;
    private TimeSpan _playDuration;
    private int _audioSyncSampleIn;
    private TimeSpan _currentTargetBufferSize;
    private CpuTimestamp _lastBufferAdjustAt;
    private bool _isOwnStream;

    private IServiceProvider Services { get; }

    private AppUIHub Hub => field ??= Services.GetRequiredService<AppUIHub>();

    private IMediaMetadataUI MediaMetadataUI => field ??= Services.GetRequiredService<IMediaMetadataUI>();
    private PlaybackLagTracker LagTracker => field ??= Services.GetRequiredService<PlaybackLagTracker>();
    private ChatAudioUI ChatAudioUI => field ??= Services.GetRequiredService<ChatAudioUI>();
    private IAudioPlaybackEngineFactory Factory { get; }

    public AudioTrackPlayer(
        string id,
        TrackInfo trackInfo,
        IMediaSource source,
        IServiceProvider services) : base(trackInfo, source, services.LogFor<AudioTrackPlayer>())
    {
        _id = id;
        Services = services;
        UpdateBufferState(true);
        Factory = services.GetRequiredService<IAudioPlaybackEngineFactory>();
    }

    [JSInvokable]
    public void OnPlaying(double offset, bool isPaused, bool isBufferLow)
    {
        DebugLog?.LogDebug(
            "[AudioTrackPlayer #{AudioTrackPlayerId}] OnPlayingAt: {Offset}, {IsPaused}, buffer: {IsBufferLow}",
            _id, offset, isPaused ? "paused" : "playing", isBufferLow ? "low" : "ok");
        UpdateBufferState(isBufferLow);
        SetPlaybackState(TimeSpan.FromSeconds(offset * TrackInfo.Speed), isPaused);
    }

    [JSInvokable("OnPresentationLag")]
    public void OnPresentationLagMs(double lagMs, double outputLatencyMs = 0, double targetBufferSizeMs = 0)
        => OnPresentationLag(TimeSpan.FromMilliseconds(lagMs), outputLatencyMs, targetBufferSizeMs);

    public void OnPresentationLag(TimeSpan lag)
        => OnPresentationLag(lag, 0, 0);

    public void OnPresentationLag(TimeSpan lag, double outputLatencyMs, double targetBufferSizeMs)
    {
        var authorId = (TrackInfo as ChatAudioTrackInfo)?.Author?.Id;
        if (authorId is null)
            return;

        var anchor = TrackInfo.SourceRecordedAt != default ? TrackInfo.SourceRecordedAt : TrackInfo.RecordedAt;
        var raw = new LagInputs(
            AnchorMs: anchor.EpochOffset.TotalMilliseconds,
            LookaheadMs: targetBufferSizeMs,
            DeviceLatencyMs: outputLatencyMs);
        LagTracker.UpdateAudio(authorId, _id, lag, raw);
    }

    [JSInvokable]
    public void OnEnded(string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"[AudioTrackPlayer #{_id}] Playback stopped with an error, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "[AudioTrackPlayer #{AudioTrackPlayerId}] Playback stopped with an error", _id);
        }
        DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] OnEnded: {Message}", _id, errorMessage);
        SetEndState(error);
    }

    protected override async ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken)
    {
        switch (command) {
            case PlayCommand:
                var trackInfo = (ChatAudioTrackInfo)TrackInfo;
                _isOwnStream = await IsOwnStream(trackInfo, cancellationToken).ConfigureAwait(false);
                await MediaMetadataUI.SetPlayback(MediaMetadata.FromTrack(trackInfo), trackInfo.IsStreaming).ConfigureAwait(false);
                var heldTrackInfo = ApplyAvSyncHold(trackInfo);
                _currentTargetBufferSize = heldTrackInfo.TargetBufferSize;
                _playbackEngine = Factory.Create(_id, heldTrackInfo, Source, this);
                await _playbackEngine.Play(cancellationToken).ConfigureAwait(false);
                break;
            case PauseCommand:
                if (_playbackEngine == null)
                    throw StandardError.AudioPlayer.PlayingStateExpected(GetType());
                await _playbackEngine.Pause(cancellationToken).ConfigureAwait(false);
                break;
            case ResumeCommand:
                if (_playbackEngine == null)
                    throw StandardError.AudioPlayer.PlayingStateExpected(GetType());
                await _playbackEngine.Resume(cancellationToken).ConfigureAwait(false);
                break;
            case AbortCommand:
                if (_playbackEngine == null)
                    throw StandardError.AudioPlayer.PlayingStateExpected(GetType());
                await _playbackEngine.End(true, cancellationToken).ConfigureAwait(false);
                break;
            case EndCommand:
                if (_playbackEngine == null)
                    throw StandardError.AudioPlayer.PlayingStateExpected(GetType());
                await _playbackEngine.End(false, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw StandardError.NotSupported(command.GetType(), "Unsupported command type.");
        }
    }

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        if (_playbackEngine == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        try {
            // During PacingDuration, push frames at real-time pace so the JS audio
            // pipeline has time to initialize (attachTrait RPC + resume).
            // In WASM (single-threaded), without pacing all frames + end command are
            // pushed before the JS run action fires, causing the feeder to end
            // without ever playing. After PacingDuration, switch to buffer-based
            // flow control (the JS side is initialized and reports isBufferLow).
            if (_playStartedAt == default)
                _playStartedAt = CpuTimestamp.Now;

            if (_playDuration < PacingDuration) {
                var framePushMoment = (_playDuration - frame.Duration - PacingHeadStartDuration).Positive();
                var delay = framePushMoment - _playStartedAt.Elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _playDuration += frame.Duration;
            var audioFrame = (AudioFrame)frame;
            ApplyAudioSync();
            await _playbackEngine.PushFrame(audioFrame, cancellationToken).ConfigureAwait(false);
            await _whenBufferLowSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException e) {
            Log.LogError(
                e,
                "[AudioTrackPlayer #{AudioTrackPlayerId}] ProcessMediaFrame: ready-to-buffer wait timed out, offset={FrameOffset}",
                _id,
                frame.Offset);
        }
    }

    protected override async Task PlayInternal(CancellationToken cancellationToken)
    {
        try {
            await base.PlayInternal(cancellationToken).ConfigureAwait(false);
        }
        finally {
            await _playbackEngine.DisposeSilentlyAsync().ConfigureAwait(false);
        }
    }

    // True when this track is the local user's own stream — its video is a
    // 0-latency local self-preview, so A/V-syncing audio to it is meaningless.
    // Computed once at start.
    private async Task<bool> IsOwnStream(ChatAudioTrackInfo trackInfo, CancellationToken cancellationToken)
    {
        if (trackInfo is not { Chat: { } chat, Author.Id: { } authorId })
            return false;
        try {
            var ownAuthor = await Hub.Authors.GetOwn(Hub.Session, chat.Id, cancellationToken).ConfigureAwait(false);
            return ownAuthor is { } own && own.Id == authorId;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "IsOwnStream check failed for {AuthorId}", authorId);
            return false;
        }
    }

    private TrackInfo ApplyAvSyncHold(ChatAudioTrackInfo trackInfo)
    {
        // Hold audio at start so it doesn't lead video: raise TargetBufferSize
        // toward (videoLag + baseline). Both playback pipelines honor
        // TargetBufferSize as the hold knob (native via DurationTargetingFrameBuffer,
        // web via the opus-decoder buffer), so no per-engine wiring is needed.
        // Gated on a live video-lag sample, so voice-only playback is untouched.
        if (!ChatAudioUI.IsAudioSyncEnabled)
            return trackInfo;
        if (trackInfo is not { Author.Id: { } authorId })
            return trackInfo;

        if (LagTracker.GetVideoLag(authorId) is not { } videoLag)
            return trackInfo;

        var maxHold = Constants.Audio.PlaybackTargetBufferSizeWithVideo + Constants.Audio.AudioSyncMaxHold;
        var hold = videoLag + Constants.Audio.AudioCatchUpBaselineDelta;
        if (hold > maxHold)
            hold = maxHold;
        return hold > trackInfo.TargetBufferSize
            ? trackInfo with { TargetBufferSize = hold }
            : trackInfo;
    }

    // Adaptive A/V hold: track video lag at runtime and lower the playback-buffer
    // target when video freshens (or raise it when video slows), so the hold
    // converges instead of fighting a stale, locked-high target. The
    // start-time ApplyAvSyncHold seeds _currentTargetBufferSize.
    private void AdjustBufferHold(AuthorId authorId)
    {
        if (LagTracker.GetVideoLag(authorId) is not { } videoLag)
            return; // No fresh video lag — keep the current hold (don't collapse the buffer).

        var baseSize = Constants.Audio.PlaybackTargetBufferSizeWithVideo;
        var cap = baseSize + Constants.Audio.AudioSyncMaxHold;
        var desired = videoLag + Constants.Audio.AudioCatchUpBaselineDelta;
        if (desired < baseSize)
            desired = baseSize;
        if (desired > cap)
            desired = cap;

        if ((desired - _currentTargetBufferSize).Duration() <= TimeSpan.FromMilliseconds(50))
            return; // Hysteresis: ignore small changes.
        if (_lastBufferAdjustAt.Elapsed < TimeSpan.FromMilliseconds(500))
            return; // Rate-limit to avoid thrash.

        _lastBufferAdjustAt = CpuTimestamp.Now;
        _currentTargetBufferSize = desired;
        _ = _playbackEngine?.SetTargetBufferSize(desired, CancellationToken.None);
    }

    // Audio plays clean at 1×; the only A/V-sync lever is the playback-buffer
    // hold (start-time ApplyAvSyncHold + runtime AdjustBufferHold). No frame is
    // ever dropped or sped up — video paces to the audio position instead.
    private void ApplyAudioSync()
    {
        if (TrackInfo is not ChatAudioTrackInfo { Author.Id: { } authorId })
            return;
        if (!ChatAudioUI.IsAudioSyncEnabled || _isOwnStream)
            return;

        if (_audioSyncSampleIn > 0) {
            _audioSyncSampleIn--;
            return;
        }
        _audioSyncSampleIn = AudioSyncPolicySamplePeriodFrames;

        AdjustBufferHold(authorId);
    }

    private void UpdateBufferState(bool isBufferLow)
    {
        if (isBufferLow)
            _whenBufferLowSource.TrySetResult();
        else {
            if (!_whenBufferLowSource.Task.IsCompleted)
                return;

            _whenBufferLowSource = TaskCompletionSourceExt.New();
        }
    }
}
