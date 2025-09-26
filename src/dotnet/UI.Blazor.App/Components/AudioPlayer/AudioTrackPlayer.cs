using ActualChat.Audio;
using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public sealed class AudioTrackPlayer : TrackPlayer, IAudioPlayerBackend
{
    private static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.AudioPlayer.create";

    private static bool DebugMode => Constants.DebugMode.AudioPlayback;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private DotNetObjectReference<IAudioPlayerBackend> _blazorRef = null!;
    private IJSObjectReference _jsRef = null!;
    private IJSObjectReference _jsRefLogging = null!;
    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();
    private volatile AsyncState<bool> _isBufferLowState = new(true);

    private IServiceProvider Services { get; }
    [field: AllowNull, MaybeNull]
    private IJSRuntime JS => field ??= Services.JSRuntime();
    [field: AllowNull, MaybeNull]
    private Dispatcher Dispatcher => field ??= Services.GetRequiredService<Dispatcher>();
    [field: AllowNull, MaybeNull]
    private IMediaMetadataUI MediaMetadataUI => field ??= Services.GetRequiredService<IMediaMetadataUI>();

    private string Id { get; }
    private Task WhenReady => _whenReadySource.Task;

    // ReSharper disable once ConvertToPrimaryConstructor
    [method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioTrackPlayer))]
    public AudioTrackPlayer(
        string id,
        TrackInfo trackInfo,
        IMediaSource source,
        IServiceProvider services)
        : base(trackInfo, source, services.LogFor<AudioTrackPlayer>())
    {
        Id = id;
        Services = services;
    }

    [JSInvokable]
    public Task OnPlaying(double offset, bool isPaused, bool isBufferLow)
    {
        DebugLog?.LogDebug(
            "[AudioTrackPlayer #{AudioTrackPlayerId}] OnPlayingAt: {Offset}, {IsPaused}, buffer: {IsBufferLow}",
            Id, offset, isPaused ? "paused" : "playing", isBufferLow ? "low" : "ok");
        SetBufferLowState(isBufferLow);
        SetPlaybackState(TimeSpan.FromSeconds(offset), isPaused);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEnded(string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"[AudioTrackPlayer #{Id}] Playback stopped with an error, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "[AudioTrackPlayer #{AudioTrackPlayerId}] Playback stopped with an error", Id);
        }
        DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] OnEnded: {Message}", Id, errorMessage);
        SetEndState(error);
        return Task.CompletedTask;
    }

    protected override async ValueTask ProcessCommand(IPlayerCommand command, CancellationToken cancellationToken)
        => await DispatchAsync(
            async () => {
                if (command is not PlayCommand && !WhenReady.IsCompletedSuccessfully)
                    await WhenReady;

                switch (command) {
                case PlayCommand:
                    if (!ReferenceEquals(_blazorRef, null)) {
                        Log.LogWarning("Repeated PlayCommand");
                        return;
                    }

                    _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                    var trackInfo = (ChatAudioTrackInfo)TrackInfo;
                    var chat = trackInfo.Chat;
                    var author = trackInfo.Author;
                    var audioSource = (AudioSource)Source;
                    var preSkip = audioSource.Format.PreSkip;
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Creating audio player in JS", Id);
                    MediaMetadataUI.SetPlayback(MediaMetadata.FromTrack(trackInfo), trackInfo.IsStreaming);
                    _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                        JSCreateMethod, CancellationToken.None,
                        _blazorRef, Id, preSkip, author.Avatar.Name, chat.Title);
                    _jsRefLogging = _jsRef.ToLogging("AudioPlayer", Log);
                    _whenReadySource.TrySetResult();
                    break;
                case PauseCommand:
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Pause command to JS", Id);
                    _ = _jsRefLogging.InvokeVoidAsync("pause", CancellationToken.None);
                    break;
                case ResumeCommand:
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Resume command to JS", Id);
                    _ = _jsRefLogging.InvokeVoidAsync("resume", CancellationToken.None);
                    break;
                case AbortCommand:
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending Abort command to JS", Id);
                    _ = _jsRefLogging.InvokeVoidAsync("end", CancellationToken.None, true);
                    break;
                case EndCommand:
                    DebugLog?.LogDebug("[AudioTrackPlayer #{AudioTrackPlayerId}] Sending End command to JS", Id);
                    _ = _jsRefLogging.InvokeVoidAsync("end", CancellationToken.None, false);
                    break;
                default:
                    throw StandardError.NotSupported(command.GetType(), "Unsupported command type.");
                }
            }).ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await DispatchAsync(
            async () => {
                if (!WhenReady.IsCompletedSuccessfully)
                    await WhenReady;
                var chunk = frame.Data;
                _ = _jsRefLogging.InvokeVoidAsync("frame", cancellationToken, chunk);
            }).ConfigureAwait(false);

    protected override Task PlayInternal(CancellationToken cancellationToken)
        => base.PlayInternal(cancellationToken)
            .ContinueWith(_ => DispatchAsync(
                async () => {
                    var (jsRef, blazorRef) = (_jsRef, _blazorRef);
                    (_jsRef, _blazorRef) = (null!, null!);
                    try {
                        try {
                            if (!ReferenceEquals(jsRef, null))
                                await jsRef.DisposeAsync();
                        }
                        finally {
                            if (!ReferenceEquals(blazorRef, null))
                                blazorRef.Dispose();
                        }
                    }
                    catch (Exception ex) {
                        Log.LogWarning(ex, "[AudioTrackPlayer #{AudioTrackPlayerId}] OnStopped failed while disposing the references", Id);
                    }
                }
            ), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private Task DispatchAsync(Func<Task> workItem)
    {
        try {
            return Dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"[AudioTrackPlayer #{{AudioTrackPlayerId}}] {nameof(DispatchAsync)} failed", Id);
            throw;
        }
    }

    private Task<TResult?> DispatchAsync<TResult>(Func<Task<TResult?>> workItem)
    {
        try {
            return Dispatcher.InvokeAsync(workItem);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"[AudioTrackPlayer #{{AudioTrackPlayerId}}] {nameof(DispatchAsync)} failed", Id);
            throw;
        }
    }

    private void SetBufferLowState(bool value)
    {
        var whenNextState = (Task?)null;
        lock (Lock) {
            if (_isBufferLowState.Value != value) {
                _isBufferLowState = _isBufferLowState.SetNext(value);
                if (!value)
                    whenNextState = _isBufferLowState.WhenNext();
            }
        }
        _ = whenNextState
            ?.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            .ContinueWith(_ => {
                if (whenNextState.IsCompleted)
                    return;

                Log.LogError("[AudioTrackPlayer #{AudioTrackPlayerId}]: buffer is not low for 10+ seconds", Id);
            }, TaskScheduler.Default);
    }
}
