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
    private IServiceProvider Services { get; } = services;
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAudioStreamingBackend Backend => field ??= Services.GetRequiredService<IAudioStreamingBackend>();
    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private RemoteAudioStreamCache RemoteAudioCache => field ??= Services.GetRequiredService<RemoteAudioStreamCache>();
    private ILogger Log => field ??= Services.LogFor<LiveAudioStreams>();

    // [ComputeMethod]
    public virtual async Task<ApiArray<LiveAudioStreamInfo>> List(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        if (!chat.Rules.Has(ChatPermissions.ReadAudio))
            return [];
        return await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
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
        return cached == null ? null : StandardRpcStream.NewAudioDelivery(cached);
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

        var diffStream = diffs.SuppressExceptions(e => e is RpcReconnectFailedException, CancellationToken.None);
        return StandardRpcStream.NewTranscriptDelivery(diffStream);
    }

    public async Task PushStream(
        Session session,
        string chatId,
        string? repliedChatEntryId,
        double clientStartAt, // Unix epoch (seconds, double)
        int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);

            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartAt, repliedEntryIdTyped);
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

    public async Task<RpcStream<MuxedStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        var muxer = new ListeningStreamMuxer(Services, chatId);
        var stream = ToLiveAsyncEnumerable(muxer, muxer.Output, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream, allowReconnect: false);
    }

    public async Task<RpcStream<MuxedStreamItem>> GetReplayStream(
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

        var muxer = new ReplayStreamMuxer(Services, session, chatId, startAt, rewindOffset, speed);
        var stream = ToReplayAsyncEnumerable(muxer, muxer.Output, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream, allowReconnect: false);
    }

    // Legacy methods

    public Task<RpcStream<MuxedStreamItem>> LegacyGetStream(
        Session session,
        ChatId chatId,
        LegacyLiveStreamSettings settings,
        CancellationToken cancellationToken)
        => GetListeningStream(session, chatId, cancellationToken);

    public Task LegacyChangeSettings(
        Session session,
        ChatId chatId,
        LegacyLiveStreamSettings settings,
        CancellationToken cancellationToken)
        => Task.CompletedTask; // No-op method

    // Private methods

    private async Task<IAsyncEnumerable<AudioFrame>?> GetOrFetchRemoteAudio(
        StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var store = RemoteAudioCache.Store;

        var stream = await store.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);

        // Dedupe so concurrent viewers on this node share a single
        // cross-shard fetch (mirrors RemoteVideoStreamCache).
        var fetched = await RemoteAudioCache
            .EnsureFetched(streamId, FetchAndPublish)
            .ConfigureAwait(false);
        if (!fetched)
            return null;

        stream = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return stream == null ? null : AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);

        async Task<bool> FetchAndPublish(StreamId sid) {
            // CT.None deliberately: detaches the cached source from this
            // viewer's lifetime so the originating viewer disconnecting
            // cannot tear down the cached stream for siblings.
            var rawRpcStream = await Backend
                .GetAudio(sid, TimeSpan.Zero, CancellationToken.None)
                .ConfigureAwait(false);
            if (rawRpcStream == null)
                return false;

            Log.LogInformation("GetOrFetchRemoteAudio: caching #{StreamId} locally", sid);
            var memoizer = rawRpcStream.Memoize(CancellationToken.None);
            if (!store.Publish(sid, memoizer))
                await memoizer.DisposeAsync().ConfigureAwait(false);
            return true;
        }
    }

    private static async IAsyncEnumerable<MuxedStreamItem> ToLiveAsyncEnumerable(
        ListeningStreamMuxer muxer,
        ChannelReader<MuxedStreamItem> reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<MuxedStreamItem> ToReplayAsyncEnumerable(
        ReplayStreamMuxer muxer,
        ChannelReader<MuxedStreamItem> reader,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally {
            await muxer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
