using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Video;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public sealed class VideoTrackPlayer : IVideoPlayerBackend, IAsyncDisposable
{
    private static bool DebugMode => Constants.DebugMode.VideoPlayback;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private readonly string _id;
    private readonly VideoStreamInfo _streamInfo;
    private readonly VideoSource _source;
    private readonly IVideoPlaybackEngineFactory _factory;
    private readonly IServiceProvider _services;
    private readonly CancellationTokenSource _stopCts;
    private readonly TaskCompletionSource _whenCompletedSource = new();

    private volatile TaskCompletionSource _whenBufferLowSource = TaskCompletionSourceExt.New();
    private IVideoPlaybackEngine? _playbackEngine;
    private Task? _playTask;

    private ILogger Log => field ??= _services.LogFor<VideoTrackPlayer>();

    public StreamId StreamId => _streamInfo.StreamId;
    public ChatId ChatId => _streamInfo.ChatId;
    public Task WhenCompleted => _whenCompletedSource.Task;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VideoTrackPlayer))]
    public VideoTrackPlayer(
        string id,
        VideoStreamInfo streamInfo,
        VideoSource source,
        IVideoPlaybackEngineFactory factory,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        _id = id;
        _streamInfo = streamInfo;
        _source = source;
        _factory = factory;
        _services = services;
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        UpdateBufferState(true);
    }

    [JSInvokable]
    public Task OnPlaying(double offset, bool isBufferLow)
    {
        DebugLog?.LogDebug(
            "[VideoTrackPlayer #{VideoTrackPlayerId}] OnPlaying: {Offset}, buffer: {IsBufferLow}",
            _id, offset, isBufferLow ? "low" : "ok");
        UpdateBufferState(isBufferLow);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEnded(string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"[VideoTrackPlayer #{_id}] Playback stopped with an error, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "[VideoTrackPlayer #{VideoTrackPlayerId}] Playback stopped with an error", _id);
        }
        DebugLog?.LogDebug("[VideoTrackPlayer #{VideoTrackPlayerId}] OnEnded: {Message}", _id, errorMessage);
        _whenCompletedSource.TrySetResult();
        return Task.CompletedTask;
    }

    public Task Play()
    {
        if (_playTask != null)
            return _playTask;

        _playTask = Task.Run(async () => {
            try {
                await PlayInternal(_stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Expected
            }
            catch (Exception ex) {
                Log.LogError(ex, "[VideoTrackPlayer #{VideoTrackPlayerId}] Playback failed", _id);
            }
            finally {
                await _playbackEngine.DisposeSilentlyAsync().ConfigureAwait(false);
                _whenCompletedSource.TrySetResult();
            }
        }, CancellationToken.None);
        return _playTask;
    }

    public async Task Stop()
    {
        await _stopCts.CancelAsync().ConfigureAwait(false);
        if (_playTask != null)
            await _playTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Stop().ConfigureAwait(false);
        _stopCts.Dispose();
    }

    private async Task PlayInternal(CancellationToken cancellationToken)
    {
        _playbackEngine = _factory.Create(_id, _streamInfo, _source, this);
        await _playbackEngine.Play(cancellationToken).ConfigureAwait(false);

        var frameCount = 0;
        await foreach (var frame in _source.GetFrames(cancellationToken).ConfigureAwait(false)) {
            await ProcessMediaFrame(frame, cancellationToken).ConfigureAwait(false);
            frameCount++;
        }

        DebugLog?.LogDebug(
            "[VideoTrackPlayer #{VideoTrackPlayerId}] Processed {FrameCount} frames",
            _id, frameCount);

        await _playbackEngine.Stop(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ProcessMediaFrame(VideoFrame frame, CancellationToken cancellationToken)
    {
        if (_playbackEngine == null)
            throw StandardError.StateTransition(GetType(), "Play should be called first.");

        try {
            await _playbackEngine.PushFrame(frame, cancellationToken).ConfigureAwait(false);
            await _whenBufferLowSource.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException e) {
            Log.LogError(
                e,
                "[VideoTrackPlayer #{VideoTrackPlayerId}] ProcessMediaFrame: ready-to-buffer wait timed out, offset={FrameOffset}",
                _id,
                frame.Offset);
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
