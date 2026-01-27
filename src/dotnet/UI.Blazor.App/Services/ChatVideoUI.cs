using System.Collections.Concurrent;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Components.VideoPanel;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;
using ActualLab.Interception;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatVideoUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub), IComputeService, INotifyInitialized
{
    private static bool DebugMode => Constants.DebugMode.VideoPlayback;
    private new ILogger? DebugLog => DebugMode ? Log : null;

    private readonly ConcurrentDictionary<StreamId, VideoTrackPlayer> _activePlayers = new();
    private readonly Lock _factoryLock = new();
    private VideoPlaybackEngineFactory? _engineFactory;

    private IRealtimeStreaming RealtimeStreaming => Hub.Services.GetRequiredService<IRealtimeStreaming>();
    private IStreamClient StreamClient => Hub.StreamClient;
    private ChatUI ChatUI => Hub.ChatUI;

    void INotifyInitialized.Initialized()
        => this.Start();

    public void SetEngineFactory(VideoPlaybackEngineFactory factory)
    {
        lock (_factoryLock)
            _engineFactory = factory;
    }

    [ComputeMethod]
    public virtual async Task<ActiveVideoStreams?> GetActiveVideoStreams(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return null;

        return await RealtimeStreaming
            .GetActiveVideoStreams(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAuthorVideoStreaming(ChatId? chatId, AuthorId? authorId, CancellationToken cancellationToken = default)
    {
        if (chatId is null || authorId is null)
            return false;

        return await RealtimeStreaming
            .IsAuthorVideoStreaming(Session, chatId, authorId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return [];

        return await RealtimeStreaming
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    public IReadOnlyCollection<VideoTrackPlayer> GetActivePlayers()
        => _activePlayers.Values.ToArray();

    public VideoTrackPlayer? GetPlayer(StreamId streamId)
        => _activePlayers.GetValueOrDefault(streamId);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);

        var baseChains = new[] {
            AsyncChain.From(WatchActiveVideoStreams),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task WatchActiveVideoStreams(CancellationToken cancellationToken)
    {
        var cSelectedChatId = await ChatUI.SelectedChatId.Computed
            .Update(cancellationToken)
            .ConfigureAwait(false);

        await foreach (var change in cSelectedChatId.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatId = change.Value;
            if (chatId is null)
                continue;

            await SubscribeToChatVideoEvents(chatId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SubscribeToChatVideoEvents(ChatId chatId, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("Subscribing to video stream events for chat {ChatId}", chatId);

        try {
            // First, check for existing active streams and start players for them
            var activeStreams = await RealtimeStreaming
                .GetActiveVideoStreams(Session, chatId, cancellationToken)
                .ConfigureAwait(false);

            if (activeStreams.Streams.Length > 0) {
                DebugLog?.LogDebug(
                    "Found {Count} existing active video streams for chat {ChatId}",
                    activeStreams.Streams.Length, chatId);

                foreach (var streamInfo in activeStreams.Streams) {
                    await OnVideoStreamStarted(streamInfo, cancellationToken).ConfigureAwait(false);
                }
            }

            // Then subscribe to new events
            var eventStream = await RealtimeStreaming
                .SubscribeToVideoStreamEvents(Session, chatId, cancellationToken)
                .ConfigureAwait(false);

            await foreach (var evt in eventStream.ConfigureAwait(false)) {
                cancellationToken.ThrowIfCancellationRequested();

                DebugLog?.LogDebug(
                    "Video stream event: {Kind} for stream {StreamId}",
                    evt.Kind, evt.StreamInfo.StreamId);

                switch (evt.Kind) {
                    case VideoStreamEventKind.Started:
                        await OnVideoStreamStarted(evt.StreamInfo, cancellationToken).ConfigureAwait(false);
                        break;
                    case VideoStreamEventKind.Ended:
                        await OnVideoStreamEnded(evt.StreamInfo.StreamId).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (Exception ex) {
            Log.LogError(ex, "Error in video stream event subscription for chat {ChatId}", chatId);
        }
    }

    private async Task OnVideoStreamStarted(VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        var streamId = streamInfo.StreamId;

        // Skip if we're already playing this stream
        if (_activePlayers.ContainsKey(streamId)) {
            DebugLog?.LogDebug("Already playing video stream {StreamId}", streamId);
            return;
        }

        // Skip our own stream - it's already shown in local preview
        var ownAuthor = await Hub.Authors.GetOwn(Session, streamInfo.ChatId, cancellationToken)
            .ConfigureAwait(false);
        if (ownAuthor?.Id == streamInfo.AuthorId) {
            DebugLog?.LogDebug("Skipping own video stream {StreamId}", streamId);
            return;
        }

        VideoPlaybackEngineFactory? factory;
        lock (_factoryLock)
            factory = _engineFactory;

        if (factory == null) {
            Log.LogWarning("VideoPlaybackEngineFactory not set, cannot play video stream {StreamId}", streamId);
            return;
        }

        try {
            DebugLog?.LogDebug(
                "Starting video playback for stream {StreamId} from author {AuthorId}",
                streamId, streamInfo.AuthorId);

            // Calculate how much to skip to catch up to real-time
            // This avoids replaying all historical frames when joining an ongoing stream
            var skipTo = Hub.Services.Clocks().SystemClock.Now - streamInfo.StartedAt;
            if (skipTo < TimeSpan.Zero)
                skipTo = TimeSpan.Zero;

            // Get video source from server - format is extracted from the first keyframe
            var videoSource = await StreamClient
                .GetVideo(streamId.Value, skipTo, cancellationToken)
                .ConfigureAwait(false);

            // Create and start video player
            var playerId = $"video-{streamId.Value}";
            var player = new VideoTrackPlayer(
                playerId,
                streamInfo,
                videoSource,
                factory,
                Hub.Services,
                cancellationToken);

            if (_activePlayers.TryAdd(streamId, player)) {
                _ = player.Play();

                // Invalidate computed state
                using (Invalidation.Begin())
                    _ = GetActiveVideoStreams(streamInfo.ChatId);

                DebugLog?.LogDebug("Video player started for stream {StreamId}", streamId);
            }
            else {
                await player.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) {
            Log.LogError(ex, "Failed to start video playback for stream {StreamId}", streamId);
        }
    }

    private async Task OnVideoStreamEnded(StreamId streamId)
    {
        if (_activePlayers.TryRemove(streamId, out var player)) {
            DebugLog?.LogDebug("Stopping video player for stream {StreamId}", streamId);

            try {
                await player.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error disposing video player for stream {StreamId}", streamId);
            }

            // Invalidate computed state - we need the ChatId but it's in VideoStreamInfo
            // We'll just invalidate for current chat
            var chatId = await ChatUI.SelectedChatId.Use(CancellationToken.None).ConfigureAwait(false);
            if (chatId is not null) {
                using (Invalidation.Begin())
                    _ = GetActiveVideoStreams(chatId);
            }
        }
    }

    public async Task StopAllPlayers()
    {
        var players = _activePlayers.Values.ToList();
        _activePlayers.Clear();

        foreach (var player in players) {
            try {
                await player.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Log.LogError(ex, "Error disposing video player {StreamId}", player.StreamId);
            }
        }
    }
}
