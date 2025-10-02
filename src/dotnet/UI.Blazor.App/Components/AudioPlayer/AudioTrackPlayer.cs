using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private static bool DebugMode => Constants.DebugMode.AudioPlayback;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private readonly string _id;
    private volatile TaskCompletionSource _whenBufferLowSource = TaskCompletionSourceExt.New();
    private IAudioPlaybackEngine? _playbackEngine;

    private IServiceProvider Services { get; }

    [field: AllowNull, MaybeNull]
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
    public Task OnPlaying(double offset, bool isPaused, bool isBufferLow)
    {
        DebugLog?.LogDebug(
            "[AudioTrackPlayer #{AudioTrackPlayerId}] OnPlayingAt: {Offset}, {IsPaused}, buffer: {IsBufferLow}",
            _id, offset, isPaused ? "paused" : "playing", isBufferLow ? "low" : "ok");
        UpdateBufferState(isBufferLow);
        SetPlaybackState(TimeSpan.FromSeconds(offset), isPaused);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEnded(string? errorMessage)
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
        return Task.CompletedTask;
    }

    protected override async ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken)
    {
        switch (command) {
            case PlayCommand:
                var trackInfo = (ChatAudioTrackInfo)TrackInfo;
                await MediaMetadataUI.SetPlayback(MediaMetadata.FromTrack(trackInfo), trackInfo.IsStreaming).ConfigureAwait(false);
                _playbackEngine = Factory.Create(_id, TrackInfo, Source, this);
                await _playbackEngine.Play(cancellationToken).ConfigureAwait(false);
                break;
            case PauseCommand:
                if (_playbackEngine == null)
                    throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                await _playbackEngine.Pause(cancellationToken).ConfigureAwait(false);
                break;
            case ResumeCommand:
                if (_playbackEngine == null)
                    throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                await _playbackEngine.Resume(cancellationToken).ConfigureAwait(false);
                break;
            case AbortCommand:
                if (_playbackEngine == null)
                    throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                await _playbackEngine.End(true, cancellationToken).ConfigureAwait(false);
                break;
            case EndCommand:
                if (_playbackEngine == null)
                    throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                await _playbackEngine.End(false, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw StandardError.NotSupported(command.GetType(), "Unsupported command type.");
        }
    }

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        if (_playbackEngine == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        try {
            await _playbackEngine.PushFrame(frame, cancellationToken).ConfigureAwait(false);
            await _whenBufferLowSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogError(
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
