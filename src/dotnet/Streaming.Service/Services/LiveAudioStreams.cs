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
    private LiveStreamAccess Access { get; } = services.GetRequiredService<LiveStreamAccess>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IServerKvasBackend ServerKvasBackend => field ??= Services.GetRequiredService<IServerKvasBackend>();
    private ICommander Commander => field ??= Services.Commander();
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
        var parsedStreamId = StreamId.Parse(streamId);
        await Access.RequireReadAudio(session, parsedStreamId, cancellationToken).ConfigureAwait(false);
        if (await IsTextOnly(parsedStreamId, cancellationToken).ConfigureAwait(false))
            return null;

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
        var parsedStreamId = StreamId.Parse(streamId);
        await Access.RequireReadAudio(session, parsedStreamId, cancellationToken).ConfigureAwait(false);
        RpcStream<TranscriptDiff>? diffs = null;
        try {
            diffs = await Backend.GetTranscript(parsedStreamId, cancellationToken).ConfigureAwait(false);
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
        AppMeters.AudioLatency.Record(ClientStats.AudioLatency(latency).TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task ReportPlaybackLag(
        Session session,
        TimeSpan audioPresentationLag,
        TimeSpan? avSyncError,
        CancellationToken cancellationToken)
    {
        _ = session;
        _ = cancellationToken;
        AppMeters.AudioPresentationLag.Record(ClientStats.PlaybackLag(audioPresentationLag).TotalMilliseconds);
        if (avSyncError is { } error)
            AppMeters.AvSyncError.Record(ClientStats.PlaybackLag(error).TotalMilliseconds);
        return Task.CompletedTask;
    }

    public async Task ReportPlayback(
        Session session, ChatId chatId, string streamId, ChatEntryId? entryId,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.Has(ChatPermissions.ReadAudio))
            return;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (!await ServerKvasBackend
                .IsPttArmed(account.Id, chatId, chat.PttEnabledAt, cancellationToken)
                .ConfigureAwait(false))
            return;

        // Client-supplied entryId is trusted: caller passed ReadAudio + armed gates, and the write is forward-only.
        var resolvedEntryId = entryId
            ?? await ResolveEntryId(chatId, streamId, cancellationToken).ConfigureAwait(false);
        if (resolvedEntryId == null || resolvedEntryId.ChatId != chatId)
            return;

        var command = new ChatPositionsBackend_Set(
            account.Id, chatId, ChatPositionKind.Heard, new ChatPosition(resolvedEntryId.LocalId));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
        Session session,
        ChatId chatId,
        Moment catchUpFrom,
        CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        chat.Rules.Require(ChatPermissions.ReadAudio);

        Log.LogInformation("GetListeningStream: chat '{ChatId}', catchUpFrom={CatchUpFrom}", chatId, catchUpFrom);
        var muxer = new ListeningStreamMuxer(Services, session, chatId, catchUpFrom);
        var stream = ToLiveAsyncEnumerable(muxer, muxer.Output, cancellationToken);
        return StandardRpcStream.NewAudioDelivery(stream, allowReconnect: false);
    }

    public async Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
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

    [Obsolete("2026.08: Use GetListeningStream - it also takes catchUpFrom.")]
    public Task<RpcStream<MuxedAudioStreamItem>> LegacyGetListeningStream(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
        => GetListeningStream(session, chatId, default, cancellationToken);

    public Task LegacyChangeSettings(
        Session session,
        ChatId chatId,
        LegacyLiveStreamSettings settings,
        CancellationToken cancellationToken)
        => Task.CompletedTask; // No-op method

    // Private methods

    private async Task<bool> IsTextOnly(StreamId streamId, CancellationToken cancellationToken)
    {
        // Isolated: an SSB caller must not depend on this.
        using var _ = Computed.BeginIsolation();
        var chatId = await Backend.GetChatId(streamId, cancellationToken).ConfigureAwait(false);
        if (chatId is not { } || chatId.Value.IsNullOrEmpty())
            return false;

        var streams = await LiveAudioBackend.List(chatId, cancellationToken).ConfigureAwait(false);
        return streams.Any(x => x.StreamId == streamId.Value && x.IsTextOnly);
    }

    private async Task<ChatEntryId?> ResolveEntryId(
        ChatId chatId, string streamId, CancellationToken cancellationToken)
    {
        if (streamId.IsNullOrEmpty())
            return null;

        for (var attempt = 0;; attempt++) {
            var entryId = await ChatsBackend.FindEntryIdByAudioStreamId(chatId, streamId, cancellationToken)
                .ConfigureAwait(false);
            if (entryId != null || attempt >= 5)
                return entryId;

            // Live acks can arrive before the transcriber creates the text entry
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IAsyncEnumerable<AudioFrame>?> GetOrFetchRemoteAudio(
        StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var store = RemoteAudioCache.Store;

        var memoizer = await store.GetMemoizer(streamId, false, cancellationToken).ConfigureAwait(false);
        if (memoizer != null)
            return Trim(memoizer);

        // Dedupe so concurrent viewers on this node share a single
        // cross-shard fetch (mirrors RemoteVideoStreamCache).
        var fetched = await RemoteAudioCache
            .EnsureFetched(streamId, FetchAndPublish)
            .ConfigureAwait(false);
        if (!fetched)
            return null;

        memoizer = await store.GetMemoizer(streamId, true, cancellationToken).ConfigureAwait(false);
        return memoizer == null ? null : Trim(memoizer);

        IAsyncEnumerable<AudioFrame> Trim(AsyncMemoizer<AudioFrame> source)
        {
            if (skipTo == Constants.Audio.SkipToLive)
                return AudioStreamingBackend.SkipToLive(source, cancellationToken);

            var replay = source.Replay(cancellationToken);
            return AudioStreamingBackend.SkipTo(replay, skipTo, cancellationToken);
        }

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

    private static async IAsyncEnumerable<MuxedAudioStreamItem> ToLiveAsyncEnumerable(
        ListeningStreamMuxer muxer,
        ChannelReader<MuxedAudioStreamItem> reader,
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

    private static async IAsyncEnumerable<MuxedAudioStreamItem> ToReplayAsyncEnumerable(
        ReplayStreamMuxer muxer,
        ChannelReader<MuxedAudioStreamItem> reader,
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
}
