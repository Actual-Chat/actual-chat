using ActualChat.Chat;
using ActualChat.Diagnostics;
using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class LiveVideoBackend : ShardComputeService, ILiveVideoBackend, IDisposable
{
    private readonly ConcurrentDictionary<ChatId, ChatState> _chatStates = new();
    private readonly StreamStore<VideoFrame> _videoStreams;

    private new ILogger Log => field ??= Services.LogFor(GetType());
    private MeshNode ThisNode => field ??= Services.MeshWatcher().ThisNode;
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private MomentClockSet Clocks => field ??= Services.Clocks();

    public LiveVideoBackend(IServiceProvider services)
        : base(services, ShardScheme.VideoBackend)
    {
        var typeFullName = GetType().FullName;
        _videoStreams = new StreamStore<VideoFrame> {
            StreamIdValidator = ValidateStreamId,
            StreamCount = AppMeters.VideoStreamCount,
            ExpirationDelay = Constants.Video.StreamExpirationDelay,
            Log = services.LogFor($"{typeFullName}.VideoStreams"),
        };

        var stopToken = ShardOwner.StopToken;
        foreach (var shardIndex in ShardScheme.ShardIndexes) {
            var shardIndexCopy = shardIndex;
            var shardState = ShardOwner.States[shardIndex].Value;
            _ = Task.Run(async () => {
                while (true) {
                    shardState = await shardState.WhenNext(stopToken).ConfigureAwait(false);
                    if (shardState.OwnershipStatus == ShardOwnershipStatus.MappedToOtherNode)
                        PurgeShard(shardIndexCopy);
                }
            }, CancellationToken.None);
        }
    }

    // [ComputeMethod]
    public virtual Task<ApiArray<VideoStreamInfo>> ListActiveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        var result = chatState.ListActiveStreams();
        Log.LogWarning("ListActiveStreams({ChatId}): returning {Count} streams", chatId, result.Count);
        return Task.FromResult(result);
    }

    // [ComputeMethod]
    public virtual Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        var result = chatState.GetStreamingAuthorIds();
        Log.LogWarning("GetVideoStreamingAuthorIds({ChatId}): returning {Count} authors: [{Authors}]",
            chatId, result.Length, string.Join(", ", result));
        return Task.FromResult(result);
    }

    // [ComputeMethod]
    public virtual Task<int> GetVideoStreamMemberCount(ChatId chatId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        return Task.FromResult(chatState.GetMemberCount());
    }

    public virtual async Task<RpcStream<VideoStreamInfo>> ObserveStreams(ChatId chatId, CancellationToken cancellationToken)
    {
        var shardState = ShardOwner.States[ShardScheme.GetShardIndex(chatId)].Value;
        var shardOwnership = await shardState.RequireShardOwnership(cancellationToken).ConfigureAwait(false);
        var linkedCts = shardOwnership.LockToken.LinkWith(cancellationToken);

        var chatState = GetChatState(chatId);
        var observations = chatState.ObserveStreams(linkedCts.Token);
        return RpcStream.New(observations, isReconnectable: false);
    }

    public virtual Task RegisterActiveStream(ChatId chatId, VideoStreamInfo streamInfo, CancellationToken cancellationToken)
    {
        Log.LogWarning("RegisterActiveStream({ChatId}): StreamId={StreamId}, AuthorId={AuthorId}",
            chatId, streamInfo.StreamId, streamInfo.AuthorId);
        var chatState = GetChatState(chatId);
        if (chatState.RegisterStream(streamInfo)) {
            Log.LogWarning("RegisterActiveStream({ChatId}): Stream registered, invalidating computed values", chatId);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
        else {
            Log.LogWarning("RegisterActiveStream({ChatId}): Stream already registered (duplicate)", chatId);
        }
        return Task.CompletedTask;
    }

    public virtual Task UnregisterActiveStream(ChatId chatId, StreamId streamId, CancellationToken cancellationToken)
    {
        Log.LogWarning("UnregisterActiveStream({ChatId}): StreamId={StreamId}", chatId, streamId);
        if (!_chatStates.TryGetValue(chatId, out var chatState))
            return Task.CompletedTask;

        if (chatState.UnregisterStream(streamId)) {
            Log.LogWarning("UnregisterActiveStream({ChatId}): Stream removed, invalidating", chatId);
            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
        }
        return Task.CompletedTask;
    }

    public virtual Task RegisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        var chatState = GetChatState(chatId);
        if (chatState.RegisterMember(sessionId))
            InvalidateGetVideoStreamMemberCount(chatId);
        return Task.CompletedTask;
    }

    public virtual Task UnregisterVideoStreamMember(ChatId chatId, string sessionId, CancellationToken cancellationToken)
    {
        if (!_chatStates.TryGetValue(chatId, out var chatState))
            return Task.CompletedTask;

        if (chatState.UnregisterMember(sessionId))
            InvalidateGetVideoStreamMemberCount(chatId);
        return Task.CompletedTask;
    }

    public void Dispose()
        => _videoStreams.Dispose();

    public virtual async Task<RpcStream<VideoFrame>?> GetVideo(StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        Log.LogInformation("GetVideo: StreamId={StreamId}, SkipTo={SkipTo}", streamId, skipTo);
        var stream = await _videoStreams.Get(streamId, cancellationToken).ConfigureAwait(false);
        if (stream == null) {
            Log.LogWarning("GetVideo: Stream {StreamId} not found in StreamStore", streamId);
            return null;
        }

        Log.LogInformation("GetVideo: Stream {StreamId} found, wrapping with logging", streamId);

        // Debug: wrap stream to count and log frames being sent to client
        var frameCount = 0;
        async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
        {
            await foreach (var frame in source.WithCancellation(cancellationToken)) {
                frameCount++;
                if (frameCount <= 3 || frameCount % 30 == 0)
                    Log.LogInformation("GetVideo sending frame #{Count}: Offset={Offset}ms, Size={Size}, IsKey={IsKey}",
                        frameCount, frame.Offset.TotalMilliseconds, frame.Data?.Length ?? 0, frame.IsKeyFrame);
                yield return frame;
            }
            Log.LogInformation("GetVideo stream ended after {Count} frames", frameCount);
        }

        stream = SkipToKeyFrame(LogFrames(stream), skipTo, cancellationToken);
        return RpcStream.New(stream);
    }

    public virtual async Task PushVideo(
        VideoRecord record,
        RpcStream<VideoFrame> videoStream,
        CancellationToken cancellationToken)
    {
        ValidateStreamId(record.StreamId);
        Log.LogTrace(nameof(PushVideo) + ": record #{StreamId} = {Record}", record.StreamId, record);

        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Video.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;

        try {
            await PushVideoInternal(record, videoStream, delayedCancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error pushing video stream {StreamId}", record.StreamId);
            throw;
        }
        finally {
            delayedCts.CancelAndDisposeSilently();
        }
    }

    // Private methods

    private async Task PushVideoInternal(
        VideoRecord record,
        IAsyncEnumerable<VideoFrame> videoFrames,
        CancellationToken cancellationToken)
    {
        var beginsAt = Clocks.SystemClock.Now;
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        // Register stream for real-time signaling
        var streamInfo = new VideoStreamInfo(
            record.StreamId,
            record.ChatId,
            author.Id,
            record.Format,
            beginsAt);
        await RegisterActiveStream(record.ChatId, streamInfo, cancellationToken)
            .ConfigureAwait(false);

        try {
            // Publish video stream for real-time viewing
            // No processing - just forward to StreamStore for memoization
            Log.LogInformation("PushVideoInternal: Publishing stream {StreamId} to StreamStore", record.StreamId);

            var frameCount = 0;
            async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
            {
                await foreach (var frame in source.WithCancellation(cancellationToken)) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 30 == 0 || frame.IsKeyFrame)
                        Log.LogInformation("PushVideoInternal frame #{Count}: Offset={Offset}ms, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Description?.Length ?? 0);
                    yield return frame;
                }
                Log.LogInformation("PushVideoInternal: stream completed with {Count} frames", frameCount);
            }

            var memoizer = LogFrames(videoFrames).SlidingMemoize(
                Constants.Video.RetentionBufferSize,
                cancellationToken);
            await _videoStreams.Publish(record.StreamId, memoizer).ConfigureAwait(false);
        }
        finally {
            // Unregister stream when it ends
            await UnregisterActiveStream(record.ChatId, record.StreamId, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private void ValidateStreamId(StreamId streamId)
    {
        if (streamId.NodeRef != ThisNode.Ref)
            throw new ArgumentOutOfRangeException(nameof(streamId),
                $"Wrong mesh node: expected {ThisNode.Ref}, but got {streamId.NodeRef}.");
    }

    private static IAsyncEnumerable<VideoFrame> SkipToKeyFrame(
        IAsyncEnumerable<VideoFrame> stream,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken; // Reserved for future use
        if (skipTo <= TimeSpan.Zero)
            return stream;

        // Skip frames until we find a keyframe at or after the requested position.
        // For video, we must start from a keyframe to decode correctly.
        return stream.SkipWhile(frame => frame.Offset < skipTo || !frame.IsKeyFrame);
    }

    private ChatState GetChatState(ChatId chatId)
        => _chatStates.GetOrAdd(chatId, static (id, self) => new ChatState(self, id), this);

    private void PurgeShard(int shardIndex)
    {
        var chatIdsToRemove = _chatStates.Keys
            .Where(chatId => ShardScheme.GetShardIndex(chatId) == shardIndex)
            .ToList();

        foreach (var chatId in chatIdsToRemove) {
            if (!_chatStates.TryRemove(chatId, out var chatState))
                continue;

            InvalidateListActiveStreams(chatId);
            InvalidateGetVideoStreamingAuthorIds(chatId);
            InvalidateGetVideoStreamMemberCount(chatId);
            chatState.Complete(RpcRerouteException.MustReroute());
        }
    }

    private void InvalidateListActiveStreams(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = ListActiveStreams(chatId, default);
    }

    private void InvalidateGetVideoStreamingAuthorIds(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetVideoStreamingAuthorIds(chatId, default);
    }

    private void InvalidateGetVideoStreamMemberCount(ChatId chatId)
    {
        using (Invalidation.Begin())
            _ = GetVideoStreamMemberCount(chatId, default);
    }
}
