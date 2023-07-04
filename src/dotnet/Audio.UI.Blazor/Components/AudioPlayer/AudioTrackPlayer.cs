using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Media;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Components;

public sealed class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioPlayback;

    private readonly string _id;
    private Dispatcher? _dispatcher;
    private DotNetObjectReference<IAudioPlayerBackend>? _blazorRef;
    private IJSObjectReference? _jsRef;
    private volatile TaskCompletionSource _whenBufferLowSource = TaskCompletionSourceExt.New();

    private IServiceProvider Services { get; }
    private IJSRuntime JS { get; }
    private Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();

    public AudioTrackPlayer(
        string id,
        IMediaSource source,
        IServiceProvider services
    ) : base(source, services.LogFor<AudioTrackPlayer>())
    {
        Services = services;
        _id = id;
        JS = services.GetRequiredService<IJSRuntime>();
        UpdateBufferState(true);
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
        DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] OnPlayEnded: {Message}", _id, errorMessage);
        SetEndState(error);
        return Task.CompletedTask;
    }

    protected override async ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken)
        => await InvokeAsync(
            async () => {
                switch (command) {
                case PlayCommand:
                    if (_jsRef != null)
                        throw StandardError.StateTransition(GetType(), "Repeated PlayCommand.");
                    _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);

                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Creating audio player in JS", _id);
                    _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                        $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                        CancellationToken.None,
                        _blazorRef,
                        _id);
                    break;
                case PauseCommand:
                    if (_jsRef == null)
                        throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Pause command to JS", _id);
                    _ = _jsRef.InvokeVoidAsync("pause", CancellationToken.None);
                    break;
                case ResumeCommand:
                    if (_jsRef == null)
                        throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Resume command to JS", _id);
                    _ = _jsRef.InvokeVoidAsync("resume", CancellationToken.None);
                    break;
                case AbortCommand:
                    if (_jsRef == null)
                        throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Abort command to JS", _id);
                    _ = _jsRef.InvokeVoidAsync("end", CancellationToken.None, true);
                    break;
                case EndCommand:
                    if (_jsRef == null)
                        throw StandardError.StateTransition(GetType(), "Start command should be called first.");
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending End command to JS", _id);
                    _ = _jsRef.InvokeVoidAsync("end", CancellationToken.None, false);
                    break;
                default:
                    throw StandardError.NotSupported(command.GetType(), "Unsupported command type.");
                }
            }).ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await InvokeAsync(
            async () => {
                if (_jsRef == null)
                    throw StandardError.StateTransition(GetType(), "Can't process media frame before initialization.");

                var chunk = frame.Data;
                _ = _jsRef.InvokeVoidAsync("frame", cancellationToken, chunk);
                try {
                    await _whenBufferLowSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) {
                    Log.LogError(
                        "[AudioTrackPlayer #{AudioTrackPlayerId}] ProcessMediaFrame: ready-to-buffer wait timed out, offset={FrameOffset}",
                        _id,
                        frame.Offset);
                }
            }).ConfigureAwait(false);

    protected override Task PlayInternal(CancellationToken cancellationToken)
        => base.PlayInternal(cancellationToken)
            .ContinueWith(_ => InvokeAsync(
                async () => {
                    var (jsRef, blazorRef) = (_jsRef, _blazorRef);
                    (_jsRef, _blazorRef) = (null, null);
                    try {
                        try {
                            if (jsRef != null)
                                await jsRef.DisposeAsync();
                        }
                        finally {
                            blazorRef?.Dispose();
                        }
                    }
                    catch (Exception ex) {
                        Log.LogWarning(ex, "[AudioTrackPlayer #{AudioTrackPlayerId}] OnStopped failed while disposing the references", _id);
                    }
                }
            ), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private Task InvokeAsync(Func<Task> workItem)
        => InvokeAsync(async () => { await workItem().ConfigureAwait(false); return true; });

#pragma warning disable RCS1229
    private Task<TResult?> InvokeAsync<TResult>(Func<Task<TResult?>> workItem)
    {
        try {
            return Dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"[AudioTrackPlayer #{{AudioTrackPlayerId}}] {nameof(InvokeAsync)} failed", _id);
            throw;
        }
    }

    private void UpdateBufferState(bool isBufferLow)
    {
        if (isBufferLow) {
            _whenBufferLowSource.TrySetResult();
        }
        else {
            if (!_whenBufferLowSource.Task.IsCompleted)
                return;

            _whenBufferLowSource = TaskCompletionSourceExt.New();
        }
    }
}
