using ActualChat.Audio;
using ActualChat.Diagnostics;
using ActualChat.Hosting;
using ActualChat.Transcription;
using ActualChat.Video;
using ActualLab.Rpc;
using ActualLab.Rpc.Infrastructure;

namespace ActualChat.Streaming.Services;

public class StreamServer(IServiceProvider services) : IStreamServer
{
    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private IAudioStreamingBackend Backend { get; } = services.GetRequiredService<IAudioStreamingBackend>();
    private IVideoStreamingBackend VideoBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private RemoteAudioStreamCache RemoteAudioCache { get; } = services.GetRequiredService<RemoteAudioStreamCache>();
    private ILogger Log { get; } = services.LogFor<StreamServer>();

    public async Task<RpcStream<AudioFrame>?> GetAudio(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var parsedStreamId = StreamId.Parse(streamId);
        var isLocal = parsedStreamId.NodeRef == MeshWatcher.ThisNode.Ref;

        if (isLocal) {
            // Local stream: return backend's RpcStream directly (already has ack settings)
            return await Backend.GetAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);
        }

        // Remote stream: fetch raw, cache locally, apply skipTo
        var cached = await GetOrFetchRemoteAudio(parsedStreamId, skipTo, cancellationToken).ConfigureAwait(false);
        return cached == null ? null : new RpcStream<AudioFrame>(cached) {
            AckPeriod = Constants.Audio.StreamAckPeriod,
            AckAdvance = Constants.Audio.StreamAckAdvance,
        };
    }

    public async Task<RpcStream<VideoFrame>?> GetVideo(string streamId, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        var parsedStreamId = StreamId.Parse(streamId);
        var peerId = RpcInboundContext.Current?.Peer.Id.ToString() ?? "rpc-unknown";
        var remoteStream = await VideoBackend.GetVideo(parsedStreamId, skipTo, peerId, cancellationToken).ConfigureAwait(false);
        return remoteStream is null
            ? null
            : new RpcStream<VideoFrame>(remoteStream) {
                AllowReconnect = false,
                AckPeriod = Constants.Video.StreamAckPeriod,
                AckAdvance = Constants.Video.StreamAckAdvance,
            };
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
        return new RpcStream<TranscriptDiff>(diffStream) {
            AckPeriod = Constants.Audio.StreamAckPeriod,
            AckAdvance = Constants.Audio.StreamAckAdvance,
        };
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
        var stopCts = new CancellationTokenSource(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        try {
            var chatIdTyped = ChatId.Parse(chatId);
            var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedChatEntryId);

            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);
            Log.LogInformation("PushAudio: {AudioRecord}", audioRecord);

            var newFrameStream = RpcStream.New(frameStream);
            await Backend.ProcessAudio(audioRecord, preSkip, newFrameStream, stopCts.Token).ConfigureAwait(false);
        }
        finally {
            // Release the remote sender on the producing peer — otherwise it keeps
            // buffering frames and its `writeFrom` hangs waiting for ACKs that we
            // will never send, because this method has exited.
            frameStream.Disconnect();
            stopCts.CancelAndDisposeSilently();
        }
    }

    public async Task PushVideo(
        Session session, string chatId,
        double clientStartOffset,
        VideoFormat format,
        RpcStream<VideoFrame> frameStream,
        StreamKind streamKind,
        CancellationToken cancellationToken)
    {
        // Live video calls: cap at Constants.Video.MaxLiveDuration (8h) rather than
        // the 3-min chat-entry duration. Every StreamKind (Webcam/Screencast) is a
        // live stream; there is no voice-message-style video path.
        using var stopCts = new CancellationTokenSource(Constants.Video.MaxLiveDuration);
        try {
            var chatIdTyped = ChatId.Parse(chatId);

            var streamId = StreamId.New(MeshWatcher.ThisNode.Ref);
            var videoRecord = new VideoRecord(streamId, session, chatIdTyped, clientStartOffset, format, streamKind);
            Log.LogInformation("PushVideo: {VideoRecord}", videoRecord);

            var newFrameStream = RpcStream.New(frameStream);
            await VideoBackend.PushVideo(videoRecord, newFrameStream, stopCts.Token).ConfigureAwait(false);
        }
        finally {
            // See PushAudio — release the remote sender on method exit.
            frameStream.Disconnect();
        }
    }

    public async Task RequestKeyFrame(string streamId, CancellationToken cancellationToken)
    {
        var sid = StreamId.Parse(streamId);
        await VideoBackend.RequestKeyFrame(sid, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VideoLatencyReportResponse> ReportVideoLatency(
        string streamId,
        VideoLatencyReport report,
        CancellationToken cancellationToken)
    {
        var parsedStreamId = StreamId.Parse(streamId);
        var peerId = RpcInboundContext.Current?.Peer.Id.ToString() ?? "rpc-unknown";
        return await VideoBackend.ReportPeerLatency(parsedStreamId, peerId, report, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task<IAsyncEnumerable<AudioFrame>?> GetOrFetchRemoteAudio(
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
        _ = BackgroundTask.Run(() => store.Publish(streamId, (IAsyncEnumerable<AudioFrame>)rawRpcStream), Log, "Error caching #{StreamId} locally", cancellationToken);
        stream = await store.Get(streamId, true, cancellationToken).ConfigureAwait(false);
        return stream == null ? null : AudioStreamingBackend.SkipTo(stream, skipTo, cancellationToken);
    }
}
