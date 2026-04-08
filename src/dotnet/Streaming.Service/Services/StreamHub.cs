using System.Buffers;
using ActualChat.Audio;
using ActualChat.Streaming.Diagnostics;
using ActualChat.Video;
using ActualChat.Hosting;
using ActualChat.Security;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Hub = Microsoft.AspNetCore.SignalR.Hub;

namespace ActualChat.Streaming.Services;

/// <summary>
/// SignalR hub for real-time audio and video streaming from clients.
/// </summary>
public class StreamHub(IServiceProvider services) : Hub
{
    private static readonly Task<string> PongTask = Task.FromResult("Pong");
    // Dedicated pool to avoid SharedArrayPool.Trim() lock contention under GC pressure
    private static readonly ArrayPool<byte> SerializationPool = ArrayPool<byte>.Create();
    // Track active video pull subscriptions per peer+stream to cancel stale pulls
    private static readonly ConcurrentDictionary<(string PeerId, string StreamId), CancellationTokenSource> ActiveVideoPulls = new();

    private readonly bool _preferThisNode = services.HostInfo().HasRole(HostRole.OneServer);

    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private IHostApplicationLifetime HostLifetime { get; } = services.HostLifetime();
    private IAudioStreamingBackend Backend { get; } = services.GetRequiredService<IAudioStreamingBackend>();
    private IVideoStreamingBackend VideoStreamingBackend { get; } = services.GetRequiredService<IVideoStreamingBackend>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log { get; } = services.LogFor<StreamHub>();

    // Currently unused
    public static Task<string> Ping()
        => PongTask;

    // The only method that is currently used by our JS client
    public Task ProcessAudioChunks(
        string sessionToken, string? chatId, string? repliedChatEntryId,
        double clientStartOffset, int preSkip,
        IAsyncEnumerable<byte[][]> audioStream)
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        => ProcessAudio(
            sessionToken,
            chatId,
            repliedChatEntryId,
            clientStartOffset,
            preSkip,
            audioStream.SelectMany(c => c.AsAsyncEnumerable()));

    // Video streaming method for JS client.
    // Each byte[] is a MessagePack-encoded VideoFrameDto.
    public Task PushVideo(
        string sessionToken,
        string? chatId,
        string codec,
        int width,
        int height,
        string? codecSettings, // Base64 encoded SPS/PPS for H.264
        double clientStartOffset,
        int streamKind, // 0 = Webcam, 1 = Screencast
        IAsyncEnumerable<byte[]> videoStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        Log.LogInformation("PushVideo: Started with codec={Codec}, {Width}x{Height}, streamKind={StreamKind}, codecSettings={CodecSettingsLen} chars",
            codec, width, height, streamKind, codecSettings?.Length ?? 0);

        // Convert raw byte[] frames to VideoFrame stream.
        // Each byte[] is a MessagePack map with camelCase string keys from the JS client.
        // We parse manually because the project's MessagePack attributes are shims.
        return PushVideoInternal(
            sessionToken,
            chatId,
            codec,
            width,
            height,
            codecSettings,
            clientStartOffset,
            (StreamKind)streamKind,
            ToVideoFrames(videoStream));

        async IAsyncEnumerable<VideoFrame> ToVideoFrames(IAsyncEnumerable<byte[]> source)
        {
            var frameCount = 0;
            var failedFrameCount = 0;
            var lastWidth = width;
            var lastHeight = height;
            await foreach (var frameBytes in source.ConfigureAwait(false)) {
                if (frameBytes.Length == 0)
                    continue;

                var tsBeforeDeserialize = Stopwatch.GetTimestamp();
                var frame = DeserializeVideoFrame(frameBytes, codec, lastWidth, lastHeight);
                var deserializeUs = Stopwatch.GetElapsedTime(tsBeforeDeserialize).TotalMicroseconds;
                StreamingMeters.VideoFrameDeserializeDuration.Record(deserializeUs);

                if (frame == null) {
                    failedFrameCount++;
                    if (failedFrameCount <= 3 || failedFrameCount % 100 == 0)
                        Log.LogWarning("PushVideo: failed to deserialize frame #{FrameCount} (total failures: {FailedCount}), bytes[0..8]={Hex}",
                            frameCount,
                            failedFrameCount,
                            Convert.ToHexString(frameBytes.AsSpan(0, Math.Min(8, frameBytes.Length))));
                    continue;
                }

                frame.CachedSerializedBytes = frameBytes; // Cache for zero-copy forwarding
                StreamingMeters.VideoFrameSizeBytes.Record(frameBytes.Length);
                StreamingMeters.VideoFramesReceived.Add(1);
                StreamingMeters.VideoBytesReceived.Add(frameBytes.Length);

                if (frame.IsKeyFrame) {
                    lastWidth = frame.Width;
                    lastHeight = frame.Height;
                }

                frameCount++;
                yield return frame;
            }
            Log.LogInformation("PushVideo: stream ended after {FrameCount} frames ({FailedCount} failed)",
                frameCount, failedFrameCount);
        }
    }

    // PLI-equivalent: receiver requests a keyframe from the sender
    public async Task RequestKeyFrame(string sessionToken, string streamId)
    {
        _ = GetSessionFromToken(sessionToken); // validate token
        var sid = StreamId.Parse(streamId);
        await VideoStreamingBackend.RequestKeyFrame(sid).ConfigureAwait(false);
    }

    // Latency reporting for JS video clients — uses same ConnectionId as GetVideo
    public async Task<double> ReportVideoLatency(
        string sessionToken,
        string streamId,
        double streamOffsetMs,
        double medianDecodeTimeMs = -1,
        int bufferDepth = -1,
        double bufferSpanMs = -1)
    {
        _ = GetSessionFromToken(sessionToken); // validate token
        var peerId = Context.ConnectionId;
        var parsedStreamId = StreamId.Parse(streamId);
        await VideoStreamingBackend.ReportPeerLatency(
            parsedStreamId, peerId, streamOffsetMs,
            medianDecodeTimeMs, bufferDepth, bufferSpanMs,
            CancellationToken.None).ConfigureAwait(false);

        // Return server timestamp for client-side RTT measurement
        return Clocks.SystemClock.UtcNow.ToUnixTimeMilliseconds();
    }

    // Video pull method for JS client — streams video frames via SignalR
    public async IAsyncEnumerable<byte[]> GetVideo(
        string sessionToken,
        string streamId,
        double skipToMs)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        var httpContext = Context.GetHttpContext()!;
        _ = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = CreateStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            yield break;

        var parsedStreamId = StreamId.Parse(streamId);
        var skipTo = TimeSpan.FromMilliseconds(skipToMs);
        var peerId = Context.ConnectionId;

        Log.LogInformation("GetVideo: #{StreamId}, SkipTo={SkipTo}, PeerId={PeerId}", parsedStreamId, skipTo, peerId);

        // Cancel any previous pull for the same peer+stream to prevent stale frames
        var pullKey = (peerId, streamId);
        var pullCts = CancellationTokenSource.CreateLinkedTokenSource(stopCts.Token);
        if (ActiveVideoPulls.TryRemove(pullKey, out var oldCts)) {
            Log.LogInformation("GetVideo: cancelling previous pull for #{StreamId}, PeerId={PeerId}", parsedStreamId, peerId);
            oldCts.CancelAndDisposeSilently();
        }
        ActiveVideoPulls[pullKey] = pullCts;

        RpcStream<VideoFrame>? rpcStream;
        try {
            rpcStream = await VideoStreamingBackend
                .GetVideo(parsedStreamId, skipTo, peerId, pullCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "GetVideo: Error getting video for stream #{StreamId}", streamId);
            ActiveVideoPulls.TryRemove(pullKey, out _);
            pullCts.CancelAndDisposeSilently();
            yield break;
        }

        if (rpcStream == null) {
            Log.LogWarning("GetVideo: Stream #{StreamId} not found", streamId);
            ActiveVideoPulls.TryRemove(pullKey, out _);
            pullCts.CancelAndDisposeSilently();
            yield break;
        }

        var frameCount = 0;
        StreamingMeters.VideoActiveConsumers.Add(1);
        try {
            await foreach (var frame in SuppressClientStreamCancellation(rpcStream, parsedStreamId, peerId, pullCts.Token).ConfigureAwait(false)) {
                frameCount++;

                byte[] bytes;
                if (frame.CachedSerializedBytes != null)
                    bytes = frame.CachedSerializedBytes;
                else {
                    var tsBeforeSerialize = Stopwatch.GetTimestamp();
                    bytes = SerializeVideoFrame(frame);
                    var serializeUs = Stopwatch.GetElapsedTime(tsBeforeSerialize).TotalMicroseconds;
                    StreamingMeters.VideoFrameSerializeDuration.Record(serializeUs);
                }
                StreamingMeters.VideoFramesSent.Add(1);
                StreamingMeters.VideoBytesSent.Add(bytes.Length);
                yield return bytes;
            }
        }
        finally {
            StreamingMeters.VideoActiveConsumers.Add(-1);
            ActiveVideoPulls.TryRemove(pullKey, out _);
            pullCts.CancelAndDisposeSilently();
        }

        Log.LogInformation("GetVideo: #{StreamId} ended after {Count} frames, PeerId={PeerId}",
            parsedStreamId, frameCount, peerId);
    }

    // Private methods

    private async Task ProcessAudio(
        string sessionToken,
        string? chatId,
        string? repliedEntryId,
        double clientStartOffset,
        int preSkip,
        IAsyncEnumerable<byte[]> audioStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var chatIdTyped = ChatId.Parse(chatId);
        var repliedEntryIdTyped = ChatEntryId.ParseNullable(repliedEntryId);
        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = CreateStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            return;

        stopCts.CancelAfter(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        var nodes = MeshWatcher.State.Value.LiveNodesByRole[HostRole.StreamingBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.StreamingBackend);
            return; // No backends
        }

        var nodeRef = _preferThisNode ? MeshWatcher.ThisNode.Ref : nodes.GetRandom().Ref;
        var streamId = StreamId.New(nodeRef);
        var audioRecord = new AudioRecord(streamId, session, chatIdTyped, clientStartOffset, repliedEntryIdTyped);

        Log.LogInformation("ProcessAudio: {AudioRecord}", audioRecord);
        var frames = audioStream
            .Select((packet, i) => new AudioFrame {
                Data = packet,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs), // we support only 20-ms packets
                Duration = Constants.Audio.OpusFrameDuration,
            })
            .SuppressCancellation(stopCts.Token);
        var frameStream = RpcStream.New(frames);
        await Backend
            .ProcessAudio(audioRecord, preSkip, frameStream, CancellationToken.None)
            .SilentAwait(false);
    }

    private async Task PushVideoInternal(
        string sessionToken,
        string? chatId,
        string codec,
        int width,
        int height,
        string? codecSettings, // Base64 encoded SPS/PPS for H.264
        double clientStartOffset,
        StreamKind streamKind,
        IAsyncEnumerable<VideoFrame> videoStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!

        var chatIdTyped = ChatId.Parse(chatId);
        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = CreateStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            return;

        stopCts.CancelAfter(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        var nodes = MeshWatcher.State.Value.LiveNodesByRole[HostRole.StreamingBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.StreamingBackend);
            return; // No backends
        }

        var nodeRef = _preferThisNode ? MeshWatcher.ThisNode.Ref : nodes.GetRandom().Ref;
        var streamId = StreamId.New(nodeRef);
        var format = new VideoFormat { Codec = codec, Width = width, Height = height, CodecSettings = codecSettings ?? "" };
        var videoRecord = new VideoRecord(streamId, session, chatIdTyped, clientStartOffset, format, streamKind);
        Log.LogInformation("PushVideo: {VideoRecord}, CodecSettings={CodecSettingsLen} chars", videoRecord, (codecSettings ?? "").Length);

        var frames = videoStream.SuppressCancellation(stopCts.Token);
        var frameStream = RpcStream.New(frames);
        await VideoStreamingBackend
            .PushVideo(videoRecord, frameStream, CancellationToken.None)
            .SilentAwait(false);
    }

    private CancellationTokenSource CreateStopTokenSource(HttpContext httpContext)
    {
        var hostStopToken = HostLifetime.StopToken();
        var stopCts = hostStopToken.LinkWith(httpContext.RequestAborted);
        if (stopCts.IsCancellationRequested && hostStopToken.IsCancellationRequested)
            Context.Abort();
        return stopCts;
    }

    private Session? GetSessionFromToken(string sessionToken)
        => sessionToken.IsNullOrEmpty() ? null
            : SecureTokensBackend.ParseSessionToken(sessionToken);

    private async IAsyncEnumerable<VideoFrame> SuppressClientStreamCancellation(
        IAsyncEnumerable<VideoFrame> source,
        StreamId streamId,
        string peerId,
        CancellationToken cancellationToken)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await using var _ = enumerator.ConfigureAwait(false);
        while (true) {
            bool moved;
            try {
                moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (HubException e) when (e.Message.StartsWith("Stream canceled by client", StringComparison.Ordinal)) {
                Log.LogDebug("GetVideo: #{StreamId} stream canceled by client, PeerId={PeerId}", streamId, peerId);
                yield break;
            }
            catch (OperationCanceledException) {
                Log.LogDebug("GetVideo: #{StreamId} canceled, PeerId={PeerId}", streamId, peerId);
                yield break;
            }
            if (!moved)
                yield break;
            yield return enumerator.Current;
        }
    }

    /// <summary>
    /// Deserialize a MessagePack-encoded video frame from the JS client.
    /// The JS client sends a map with camelCase string keys and numeric ticks.
    /// We parse manually because the project's MessagePack attributes are shims
    /// (MemoryPack is the primary serializer).
    /// </summary>
    private static VideoFrame? DeserializeVideoFrame(byte[] bytes, string fallbackCodec, int fallbackWidth = 0, int fallbackHeight = 0)
    {
        try {
            var reader = new MessagePackReader(bytes);
            var mapLen = reader.ReadMapHeader();

            long offset = 0;
            long duration = 0;
            var isKeyFrame = false;
            var width = 0;
            var height = 0;
            byte[]? data = null;
            byte[]? description = null;
            string? codec = null;
            var temporalLayerId = 0;

            for (var i = 0; i < mapLen; i++) {
                var key = reader.ReadString();
                switch (key) {
                    case "offset":
                        offset = reader.ReadInt64();
                        break;
                    case "duration":
                        duration = reader.ReadInt64();
                        break;
                    case "isKeyFrame":
                        isKeyFrame = reader.ReadBoolean();
                        break;
                    case "width":
                        width = reader.ReadInt32();
                        break;
                    case "height":
                        height = reader.ReadInt32();
                        break;
                    case "data":
                        // Skip the byte[] copy — CachedSerializedBytes holds the original payload
                        // for zero-copy fan-out. Just skip past the data in the reader.
                        reader.ReadBytes();
                        break;
                    case "description":
                        description = reader.TryReadNil() ? null : reader.ReadBytes()?.ToArray();
                        break;
                    case "codec":
                        codec = reader.TryReadNil() ? null : reader.ReadString();
                        break;
                    case "temporalLayerId":
                        temporalLayerId = reader.ReadInt32();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new VideoFrame(isKeyFrame) {
                Data = data ?? [],
                Offset = new TimeSpan(offset),
                Duration = new TimeSpan(duration),
                Width = width != 0 ? width : fallbackWidth,
                Height = height != 0 ? height : fallbackHeight,
                Description = description,
                Codec = isKeyFrame ? (codec ?? fallbackCodec) : codec,
                TemporalLayerId = temporalLayerId,
            };
        }
        catch {
            return null;
        }
    }

    private static byte[] SerializeVideoFrame(VideoFrame frame)
    {
        using var buffer = new ArrayPoolBuffer<byte>(SerializationPool, 1024, mustClear: false);
        var writer = new MessagePackWriter(buffer);

        var fieldCount = 3; // offset, duration, data
        if (frame.IsKeyFrame) fieldCount += 3; // isKeyFrame, width, height
        if (frame.Description != null) fieldCount++;
        if (frame.Codec != null) fieldCount++;
        if (frame.TemporalLayerId > 0) fieldCount++;

        writer.WriteMapHeader(fieldCount);

        writer.Write("offset");
        writer.Write(frame.Offset.Ticks);
        writer.Write("duration");
        writer.Write(frame.Duration.Ticks);
        writer.Write("data");
        writer.Write(frame.Data);

        if (frame.IsKeyFrame) {
            writer.Write("isKeyFrame");
            writer.Write(true);
            writer.Write("width");
            writer.Write(frame.Width);
            writer.Write("height");
            writer.Write(frame.Height);
        }
        if (frame.Description != null) {
            writer.Write("description");
            writer.Write(frame.Description);
        }
        if (frame.Codec != null) {
            writer.Write("codec");
            writer.Write(frame.Codec);
        }
        if (frame.TemporalLayerId > 0) {
            writer.Write("temporalLayerId");
            writer.Write(frame.TemporalLayerId);
        }

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}
