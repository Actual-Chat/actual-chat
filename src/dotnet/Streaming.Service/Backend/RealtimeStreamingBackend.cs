using System.Threading.Channels;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public class RealtimeStreamingBackend : IRealtimeStreamingBackend
{
    private readonly ConcurrentDictionary<ChatId, ConcurrentDictionary<StreamId, VideoStreamInfo>> _activeStreams = new();
    private readonly ConcurrentDictionary<ChatId, long> _versions = new();
    private readonly ConcurrentDictionary<ChatId, HashSet<ChannelWriter<VideoStreamEvent>>> _subscribers = new();
    private readonly object _subscribersLock = new();

    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public RealtimeStreamingBackend(IServiceProvider services)
    {
        Commander = services.Commander();
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
    }

    // [ComputeMethod]
    public virtual Task<ActiveVideoStreams> GetActiveVideoStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatStreams = _activeStreams.GetValueOrDefault(chatId);
        var streams = chatStreams?.Values.ToArray() ?? [];
        var version = _versions.GetValueOrDefault(chatId);
        return Task.FromResult(new ActiveVideoStreams(chatId, version, streams));
    }

    // [ComputeMethod]
    public virtual Task<VideoStreamInfo?> GetVideoStreamInfo(StreamId streamId, CancellationToken cancellationToken)
    {
        foreach (var chatStreams in _activeStreams.Values) {
            if (chatStreams.TryGetValue(streamId, out var info))
                return Task.FromResult<VideoStreamInfo?>(info);
        }
        return Task.FromResult<VideoStreamInfo?>(null);
    }

    // [ComputeMethod]
    public virtual Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatStreams = _activeStreams.GetValueOrDefault(chatId);
        if (chatStreams == null || chatStreams.IsEmpty)
            return Task.FromResult(Array.Empty<AuthorId>());

        var authorIds = chatStreams.Values
            .Select(s => s.AuthorId)
            .Distinct()
            .ToArray();
        return Task.FromResult(authorIds);
    }

    // [ComputeMethod]
    public virtual Task<bool> IsAuthorVideoStreaming(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var chatStreams = _activeStreams.GetValueOrDefault(chatId);
        if (chatStreams == null || chatStreams.IsEmpty)
            return Task.FromResult(false);

        var isStreaming = chatStreams.Values.Any(s => s.AuthorId == authorId);
        return Task.FromResult(isStreaming);
    }

    public Task<RpcStream<VideoStreamEvent>> SubscribeToVideoStreamEvents(ChatId chatId, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<VideoStreamEvent>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_subscribersLock) {
            if (!_subscribers.TryGetValue(chatId, out var writers)) {
                writers = new HashSet<ChannelWriter<VideoStreamEvent>>();
                _subscribers[chatId] = writers;
            }
            writers.Add(channel.Writer);
        }

        // Clean up when cancellation is requested
        cancellationToken.Register(() => {
            lock (_subscribersLock) {
                if (_subscribers.TryGetValue(chatId, out var writers)) {
                    writers.Remove(channel.Writer);
                    if (writers.Count == 0)
                        _subscribers.TryRemove(chatId, out _);
                }
            }
            channel.Writer.TryComplete();
        });

        var stream = channel.Reader.ReadAllAsync(cancellationToken);
        return Task.FromResult(RpcStream.New(stream));
    }

    // [CommandHandler]
    public virtual async Task OnRegisterVideoStream(
        RealtimeStreamingBackend_RegisterVideoStream command,
        CancellationToken cancellationToken)
    {
        var streamInfo = command.StreamInfo;
        var chatId = streamInfo.ChatId;

        if (Invalidation.IsActive) {
            _ = GetActiveVideoStreams(chatId, default);
            _ = GetVideoStreamInfo(streamInfo.StreamId, default);
            _ = GetVideoStreamingAuthorIds(chatId, default);
            _ = IsAuthorVideoStreaming(chatId, streamInfo.AuthorId, default);
            return;
        }

        // Add stream to active streams
        var chatStreams = _activeStreams.GetOrAdd(chatId, _ => new ConcurrentDictionary<StreamId, VideoStreamInfo>());
        chatStreams[streamInfo.StreamId] = streamInfo;

        // Increment version
        _versions.AddOrUpdate(chatId, 1, (_, v) => v + 1);

        // Broadcast event to subscribers
        var evt = new VideoStreamEvent(VideoStreamEventKind.Started, streamInfo);
        await BroadcastEvent(chatId, evt).ConfigureAwait(false);

        Log.LogInformation("Video stream registered: {StreamId} in chat {ChatId}", streamInfo.StreamId, chatId);
    }

    // [CommandHandler]
    public virtual async Task OnUnregisterVideoStream(
        RealtimeStreamingBackend_UnregisterVideoStream command,
        CancellationToken cancellationToken)
    {
        var streamId = command.StreamId;
        var chatId = command.ChatId;

        if (Invalidation.IsActive) {
            _ = GetActiveVideoStreams(chatId, default);
            _ = GetVideoStreamInfo(streamId, default);
            _ = GetVideoStreamingAuthorIds(chatId, default);
            // Note: We don't know the AuthorId here, so we invalidate all authors
            // The actual implementation would need to track this
            return;
        }

        // Remove stream from active streams
        if (!_activeStreams.TryGetValue(chatId, out var chatStreams))
            return;

        if (!chatStreams.TryRemove(streamId, out var removedInfo))
            return;

        // Clean up empty chat entries
        if (chatStreams.IsEmpty)
            _activeStreams.TryRemove(chatId, out _);

        // Increment version
        _versions.AddOrUpdate(chatId, 1, (_, v) => v + 1);

        // Broadcast event to subscribers
        var evt = new VideoStreamEvent(VideoStreamEventKind.Ended, removedInfo);
        await BroadcastEvent(chatId, evt).ConfigureAwait(false);

        // Invalidate IsAuthorVideoStreaming for the removed stream's author
        using (Invalidation.Begin())
            _ = IsAuthorVideoStreaming(chatId, removedInfo.AuthorId, default);

        Log.LogInformation("Video stream unregistered: {StreamId} in chat {ChatId}", streamId, chatId);
    }

    private Task BroadcastEvent(ChatId chatId, VideoStreamEvent evt)
    {
        HashSet<ChannelWriter<VideoStreamEvent>>? writers;
        lock (_subscribersLock) {
            if (!_subscribers.TryGetValue(chatId, out writers) || writers.Count == 0)
                return Task.CompletedTask;
            writers = new HashSet<ChannelWriter<VideoStreamEvent>>(writers); // Copy to avoid lock during iteration
        }

        foreach (var writer in writers) {
            if (!writer.TryWrite(evt)) {
                Log.LogWarning("Failed to write video stream event to subscriber channel");
            }
        }
        return Task.CompletedTask;
    }
}
