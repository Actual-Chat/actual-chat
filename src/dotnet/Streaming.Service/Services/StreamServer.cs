using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private IAudioStreamingBackend Backend { get; } = services.GetRequiredService<IAudioStreamingBackend>();
    private IVideoStreamingBackend VideoBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private RemoteAudioStreamCache RemoteAudioCache { get; } = services.GetRequiredService<RemoteAudioStreamCache>();
    private ILogger Log { get; } = services.LogFor<StreamServer>();

    public async Task<RpcStream<byte[]>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var parsedStreamId = StreamId.Parse(streamId);
        var isLocal = parsedStreamId.NodeRef == MeshWatcher.ThisNode.Ref;

        if (isLocal) {
            // Local stream: use backend directly
            var source = await Backend.GetAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);
            return source == null ? null : RpcStream.New(source);
        }

        // Remote stream: fetch raw, cache locally, apply skipTo
        var cached = await GetOrFetchRemoteAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);
        return cached == null ? null : RpcStream.New(cached);
    }

    public async Task<RpcStream<TranscriptDiff>?> GetTranscript(string streamId, CancellationToken cancellationToken)
    {
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
        return RpcStream.New(diffStream);
    }

    public Task ReportAudioLatency(TimeSpan latency, CancellationToken cancellationToken)
    {
        AppMeters.AudioLatency.Record(latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public async Task PushAudio(
        Session session, string chatId, string? repliedChatEntryId,
        double clientStartOffset, int preSkip,
        RpcStream<AudioFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);

        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);
        var newFrameStream = RpcStream.New(frameStream);
        Log.LogInformation("PushAudio: {AudioRecord}", audioRecord);

        using var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        await Backend.ProcessAudio(audioRecord, preSkip, newFrameStream, stopCts.Token).ConfigureAwait(false);
    }

    public async Task PushVideo(
        Session session, string chatId,
        double clientStartOffset,
        VideoFormat format,
        string? continuationOf,
        RpcStream<VideoFrame> frameStream,
        CancellationToken cancellationToken)
    {
        var chatIdTyped = ChatId.Parse(chatId);

        var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
        var continuationOfId = StreamId.ParseNullable(continuationOf);
        var videoRecord = new VideoRecord(
            streamId,
            session,
            chatIdTyped,
            clientStartOffset,
            format,
            StreamKind.Webcam,
            continuationOfId);
        var newFrameStream = RpcStream.New(frameStream);
        Log.LogInformation("PushVideo: {VideoRecord}", videoRecord);

        using var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        await VideoBackend.PushVideo(videoRecord, newFrameStream, stopCts.Token).ConfigureAwait(false);
    }


    // Private methods

    private async Task<IAsyncEnumerable<byte[]>?> GetOrFetchRemoteAudio(
        StreamId streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var store = RemoteAudioCache.Store;

        // Fast path: already cached locally
        var stream = await store.Get(streamId, false, cancellationToken).ConfigureAwait(false);
        if (stream != null)
            return AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);

        // Fetch from remote backend via RPC (skipTo=0 to get full stream for caching)
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
        _ = BackgroundTask.Run(() => store.Publish(streamId, (IAsyncEnumerable<byte[]>)rawRpcStream), Log, "Error caching #{StreamId} locally", cancellationToken);
        stream = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return stream == null ? null : AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);
    }
}
