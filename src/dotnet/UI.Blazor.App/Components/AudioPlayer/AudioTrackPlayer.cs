using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private static bool DebugMode => Constants.DebugMode.AudioTrackPlayer;
    private ILogger? DebugLog => DebugMode ? Log : null;

    // Pacing: during the initial PacingDuration, push frames at real-time pace
    // so the JS audio pipeline has time to initialize (attachTrait + resume).
    // After PacingDuration, switch to buffer-based flow control.
    private static readonly TimeSpan PacingDuration = TimeSpan.FromMilliseconds(150);

    private readonly string _id;
    private IAudioPlaybackEngine? _playbackEngine;
    private volatile TaskCompletionSource _whenBufferLowSource = TaskCompletionSourceExt.New();
    private CpuTimestamp _playStartedAt;
    private TimeSpan _playDuration;
    private string? _authorId;
    private double _recordedAtMs;
    private bool _reportSyncToJs;

    private IServiceProvider Services { get; }

    private IMediaMetadataUI MediaMetadataUI => field ??= Services.GetRequiredService<IMediaMetadataUI>();
    private IAudioPlaybackEngineFactory Factory { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioTrackPlayer))]
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
        if (_reportSyncToJs) {
            var state = isPaused ? "paused" : "playing";
            _ = Services.JSRuntime().InvokeVoidAsync(
                "blazorApp.AudioVideoSync.update",
                CancellationToken.None,
                _authorId, offset, _recordedAtMs, state);
        }
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
        if (_reportSyncToJs) {
            _ = Services.JSRuntime().InvokeVoidAsync(
                "blazorApp.AudioVideoSync.clear",
                CancellationToken.None,
                _authorId);
            _reportSyncToJs = false;
        }
        SetEndState(error);
    }

    protected override async ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken)
    {
        switch (command) {
            case PlayCommand:
                var trackInfo = (ChatAudioTrackInfo)TrackInfo;
                await MediaMetadataUI.SetPlayback(MediaMetadata.FromTrack(trackInfo), trackInfo.IsStreaming).ConfigureAwait(false);
                _playbackEngine = Factory.Create(_id, TrackInfo, Source, this);
                if (Services.HostInfo().HostKind.IsMauiApp()) {
                    _authorId = trackInfo.Author?.Id.Value;
                    _recordedAtMs = trackInfo.RecordedAt.EpochOffset.TotalMilliseconds;
                    _reportSyncToJs = _authorId != null;
                }
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
                // Wait until this frame's expected play time (minus one frame duration)
                var framePushMoment = (_playDuration - frame.Duration).Positive();
                var delay = framePushMoment - _playStartedAt.Elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            _playDuration += frame.Duration;
            await _playbackEngine.PushFrame(frame, cancellationToken).ConfigureAwait(false);
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
