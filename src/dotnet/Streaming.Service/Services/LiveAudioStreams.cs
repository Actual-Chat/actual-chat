using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Live;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// RPC service for audio frame push/pull, transcripts, and the multiplexed
/// real-time / replay live-stream feed.
/// </summary>
public class LiveAudioStreams(IServiceProvider services) : ILiveAudioStreams
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<(Session, ChatId), LiveStreamMuxer> _liveMuxers = new();
    private readonly ConcurrentDictionary<(Session, ChatId), ReplayStreamMuxer> _replayMuxers = new();

    private IServiceProvider Services { get; } = services;
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAudioStreamingBackend Backend => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private RemoteAudioStreamCache RemoteAudioCache => field ??= Services.GetRequiredService<RemoteAudioStreamCache>();
    private ILiveAudioBackend LiveBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private ILogger Log => field ??= Services.LogFor<LiveAudioStreams>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveStreamInfo>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.Has(ChatPermissions.ReadAudio))
            return [];
        return await LiveBackend.List(chatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<AudioFrame>?> GetStream(
        Session session,
        string streamId,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // Stream-level access is gated upstream via RegisterMember equivalents.
        _ = session;
        var parsedStreamId = StreamId.Parse(streamId);
        var isLocal = parsedStreamId.NodeRef == MeshWatcher.ThisNode.Ref;

        if (isLocal)
            return await Backend.GetAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);

        var cached = await GetOrFetchRemoteAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);
        return cached == null ? null : new RpcStream<AudioFrame>(cached) {
            AckPeriod = Constants.Audio.StreamAckPeriod,
            BufferSize = Constants.Audio.StreamBufferSize,
        };
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
        Session session,
        string streamId,
        CancellationToken cancellationToken)
    {
        _ = session;
        RpcStream<TranscriptDiff>? diffs = null;
        try {
            diffs = await Backend.GetTranscript(StreamId.Parse(streamId), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (RpcReconnectFailedException) { }
        catch (Exception e) {
            Log.LogError(e, "Error getting transcript for stream #{StreamId}", streamId);
        }

        if (diffs == null)
            return null;

        var diffStream = diffs
            .SuppressException<TranscriptDiff, RpcReconnectFailedException>(cancellationToken)
            .SuppressCancellation(cancellationToken);
        return new RpcStream<TranscriptDiff>(diffStream) {
            AckPeriod = Constants.Audio.StreamAckPeriod,
            BufferSize = Constants.Audio.StreamBufferSize,
        };
    }

    public async Task PushStream(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double sourceStartOffsetSeconds,
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);

            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var audioRecord = new AudioRecord(streamId, session, chatIdTyped, sourceStartOffsetSeconds, repliedEntryIdTyped);
            Log.LogInformation("PushStream: {AudioRecord}", audioRecord);

            var newFrameStream = RpcStream.New(frameStream);
            await Backend.ProcessAudio(audioRecord, preSkip, newFrameStream, stopCts.Token).ConfigureAwait(false);
        }
        finally {
            // Release the remote sender on method exit so its writeFrom doesn't hang.
            frameStream.Disconnect();
            stopCts.CancelAndDisposeSilently();
        }
    }

    public Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken cancellationToken)
    {
        _ = session;
        _ = cancellationToken;
        AppMeters.AudioLatency.Record(latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public async Task<RpcStream<LiveStreamItem>> LegacyGetStream(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        LiveStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) { // TODO(AY): Make it more efficient later?
            if (_liveMuxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync(); // No need to await for this here

            muxer = new LiveStreamMuxer(Services, chatId, settings);
            _liveMuxers[key] = muxer;
        }

        var stream = ToLiveAsyncEnumerable(key, muxer, muxer.Output, cancellationToken);
        return new RpcStream<LiveStreamItem>(stream) {
            AllowReconnect = false,
            AckPeriod = Constants.Audio.StreamAckPeriod,
            BufferSize = Constants.Audio.StreamBufferSize,
        };
    }

    public async Task ChangeSettings(
        Session session,
        ChatId chatId,
        LiveStreamSettings settings,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        if (_liveMuxers.TryGetValue((session, chatId), out var muxer))
            muxer.UpdateConfig(settings);
    }

    public async Task<RpcStream<LiveStreamItem>> GetReplayStream(
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        ReplayStreamMuxer muxer;
        var key = (session, chatId);
        lock (_lock) {
            if (_replayMuxers.TryRemove(key, out var oldMuxer))
                _ = oldMuxer.DisposeSilentlyAsync();

            muxer = new ReplayStreamMuxer(Services, session, chatId, startAt, rewindOffset, speed);
            _replayMuxers[key] = muxer;
        }

        var stream = ToReplayAsyncEnumerable(key, muxer, muxer.Output, cancellationToken);
        return new RpcStream<LiveStreamItem>(stream) {
            AllowReconnect = false,
            AckPeriod = Constants.Audio.StreamAckPeriod,
            BufferSize = Constants.Audio.StreamBufferSize,
        };
    }

    // Private methods

    private async Task<IAsyncEnumerable<AudioFrame>?> GetOrFetchRemoteAudio(
        StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var store = RemoteAudioCache.Store;

        var stream = await store.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);

        var rawRpcStream = await Backend
            .GetAudio(streamId, TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);
        if (rawRpcStream == null)
            return null;

        Log.LogInformation("GetOrFetchRemoteAudio: caching #{StreamId} locally", streamId);
        // Publish returns memoizer.WriteTask which only completes when the source
        // stream ends — do NOT await it here, or every remote peer will block until
        // the speaker stops talking. The memoizer is registered in the store
        // synchronously before Publish returns, so the subsequent Get succeeds
        // immediately.
        _ = BackgroundTask.Run(() => store.Publish(streamId, (IAsyncEnumerable<AudioFrame>)rawRpcStream), Log, "Error caching #{StreamId} locally", cancellationToken);
        stream = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return stream == null ? null : AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);
    }

    private async IAsyncEnumerable<LiveStreamItem> ToLiveAsyncEnumerable(
        (Session, ChatId) key,
        LiveStreamMuxer originalMuxer,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            // Only remove if the muxer in the dictionary is still the one we started with.
            // A new GetStream call may have replaced it; removing the replacement would be wrong.
            // Use lock to avoid TOCTOU race with GetStream (which also holds _lock).
            bool shouldDispose;
            lock (_lock) {
                shouldDispose = _liveMuxers.TryGetValue(key, out var current)
                    && ReferenceEquals(current, originalMuxer)
                    && _liveMuxers.TryRemove(key, out _);
            }
            if (shouldDispose)
                await originalMuxer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<LiveStreamItem> ToReplayAsyncEnumerable(
        (Session, ChatId) key,
        ReplayStreamMuxer originalMuxer,
        ChannelReader<LiveStreamItem> reader,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            bool shouldDispose;
            lock (_lock) {
                shouldDispose = _replayMuxers.TryGetValue(key, out var current)
                    && ReferenceEquals(current, originalMuxer)
                    && _replayMuxers.TryRemove(key, out _);
            }
            if (shouldDispose)
                await originalMuxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
