using System.Buffers;
using ActualChat.Audio;
using ActualChat.Video;
using ActualChat.Hosting;
using ActualChat.Security;
using ActualLab.Rpc;
using MessagePack;
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

    private readonly bool _preferThisNode = services.HostInfo().HasRole(HostRole.OneServer);

    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private IHostApplicationLifetime HostLifetime { get; } = services.HostLifetime();
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();
    private ILiveVideoBackend LiveVideoBackend { get; } = services.GetRequiredService<ILiveVideoBackend>();
    private ILogger Log { get; } = services.LogFor<StreamHub>();

    // Currently unused
    public static Task<string> Ping()
        => PongTask;

    // The only method that is currently used by our JS client
    public Task ProcessAudioChunks(
        string sessionToken,
        string? chatId,
        string? repliedChatEntryId,
        double clientStartOffset,
        int preSkip,
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
    // Uses byte[][] batches (like audio) to avoid MessagePack deserialization issues
    // with VideoFrame's TimeSpan properties and [MessagePackObject] inheritance.
    // Each byte[] in the batch is a MessagePack-encoded VideoFrameDto.
    public Task PushVideo(
        string sessionToken,
        string? chatId,
        string codec,
        int width,
        int height,
        string? codecSettings, // Base64 encoded SPS/PPS for H.264
        double clientStartOffset,
        IAsyncEnumerable<byte[][]> videoStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        Log.LogInformation("PushVideo: Started with codec={Codec}, {Width}x{Height}, codecSettings={CodecSettingsLen} chars",
            codec, width, height, codecSettings?.Length ?? 0);

        // Convert raw byte[][] batches to VideoFrame stream.
        // Each byte[] is a MessagePack map with camelCase string keys from the JS client.
        // We parse manually because the project's MessagePack attributes are shims.
        async IAsyncEnumerable<VideoFrame> ToVideoFrames(IAsyncEnumerable<byte[][]> source)
        {
            var batchCount = 0;
            var frameCount = 0;
            await foreach (var batch in source) {
                batchCount++;
                if (batch.Length == 0)
                    continue;

                if (batchCount <= 3 || batchCount % 30 == 0)
                    Log.LogInformation("PushVideo: batch #{BatchCount} with {Count} frames", batchCount, batch.Length);

                foreach (var frameBytes in batch) {
                    if (frameBytes.Length == 0)
                        continue;

                    var frame = DeserializeVideoFrame(frameBytes, codec);
                    if (frame == null) {
                        if (batchCount <= 3)
                            Log.LogWarning("PushVideo: failed to deserialize frame in batch #{BatchCount}, bytes[0..8]={Hex}",
                                batchCount,
                                Convert.ToHexString(frameBytes.AsSpan(0, Math.Min(8, frameBytes.Length))));
                        continue;
                    }

                    frameCount++;
                    if (frameCount <= 3 || frameCount % 30 == 0 || frame.IsKeyFrame)
                        Log.LogInformation("PushVideo frame #{Count}: Offset={Offset}ms, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, frame.Offset.TotalMilliseconds, frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Description?.Length ?? 0);

                    yield return frame;
                }
            }
            Log.LogInformation("PushVideo: stream ended after {BatchCount} batches, {FrameCount} total frames", batchCount, frameCount);
        }

        return PushVideoInternal(
            sessionToken,
            chatId,
            codec,
            width,
            height,
            codecSettings,
            clientStartOffset,
            ToVideoFrames(videoStream));
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
        var repliedEntryIdTyped = TextEntryId.ParseNullable(repliedEntryId);
        var httpContext = Context.GetHttpContext()!;
        var session = GetSessionFromToken(sessionToken) ?? httpContext.GetSessionFromCookie();

        using var stopCts = CreateStopTokenSource(httpContext);
        if (stopCts.IsCancellationRequested)
            return;

        stopCts.CancelAfter(Constants.Chat.MaxEntryDuration + TimeSpan.FromSeconds(5));
        var nodes = MeshWatcher.State.Value.LiveNodesByRole[HostRole.AudioBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.AudioBackend);
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
        var nodes = MeshWatcher.State.Value.LiveNodesByRole[HostRole.VideoBackend];
        if (nodes.Length == 0) {
            Log.LogError("No nodes serving {Role} role!", HostRole.VideoBackend);
            return; // No backends
        }

        var nodeRef = _preferThisNode ? MeshWatcher.ThisNode.Ref : nodes.GetRandom().Ref;
        var streamId = StreamId.New(nodeRef);
        var format = new VideoFormat { Codec = codec, Width = width, Height = height, CodecSettings = codecSettings ?? "" };
        var videoRecord = new VideoRecord(streamId, session, chatIdTyped, clientStartOffset, format);
        Log.LogInformation("PushVideo: {VideoRecord}, CodecSettings={CodecSettingsLen} chars", videoRecord, (codecSettings ?? "").Length);

        var frames = videoStream.SuppressCancellation(stopCts.Token);
        var frameStream = RpcStream.New(frames);
        await LiveVideoBackend
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

    /// <summary>
    /// Deserialize a MessagePack-encoded video frame from the JS client.
    /// The JS client sends a map with camelCase string keys and numeric ticks.
    /// We parse manually because the project's MessagePack attributes are shims
    /// (MemoryPack is the primary serializer).
    /// </summary>
    private static VideoFrame? DeserializeVideoFrame(byte[] bytes, string fallbackCodec)
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
                        data = reader.ReadBytes()?.ToArray();
                        break;
                    case "description":
                        description = reader.TryReadNil() ? null : reader.ReadBytes()?.ToArray();
                        break;
                    case "codec":
                        codec = reader.TryReadNil() ? null : reader.ReadString();
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
                Width = width,
                Height = height,
                Description = description,
                Codec = isKeyFrame ? (codec ?? fallbackCodec) : codec,
            };
        }
        catch {
            return null;
        }
    }
}
