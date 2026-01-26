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

// DTO that matches the TypeScript VideoStreamFrame interface exactly
// Uses camelCase property names and primitive types for MessagePack compatibility
[MessagePackObject]
public class VideoStreamFrameDto
{
    [MessagePack.Key("offset")]
    public double Offset { get; set; }

    [MessagePack.Key("duration")]
    public double Duration { get; set; }

    [MessagePack.Key("isKeyFrame")]
    public bool IsKeyFrame { get; set; }

    [MessagePack.Key("width")]
    public int Width { get; set; }

    [MessagePack.Key("height")]
    public int Height { get; set; }

    [MessagePack.Key("data")]
    public byte[] Data { get; set; } = [];

    [MessagePack.Key("description")]
    public byte[]? Description { get; set; }

    public VideoFrame ToVideoFrame()
        => new(IsKeyFrame) {
            // TypeScript sends microseconds, convert to TimeSpan
            Offset = TimeSpan.FromMicroseconds(Offset),
            Duration = TimeSpan.FromMicroseconds(Duration),
            Width = Width,
            Height = Height,
            Data = Data,
            Description = Description,
        };
}

/// <summary>
/// SignalR hub for real-time audio streaming from clients.
/// </summary>
public class StreamHub(IServiceProvider services) : Hub
{
    private static readonly Task<string> PongTask = Task.FromResult("Pong");

    private readonly bool _preferThisNode = services.HostInfo().HasRole(HostRole.OneServer);

    private MeshWatcher MeshWatcher { get; } = services.MeshWatcher();
    private ISecureTokensBackend SecureTokensBackend { get; } = services.GetRequiredService<ISecureTokensBackend>();
    private IHostApplicationLifetime HostLifetime { get; } = services.HostLifetime();
    private IStreamingBackend Backend { get; } = services.GetRequiredService<IStreamingBackend>();
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

    // Video streaming method for JS client
    public Task PushVideo(
        string sessionToken,
        string? chatId,
        string codec,
        int width,
        int height,
        string? codecSettings, // Base64 encoded SPS/PPS for H.264
        double clientStartOffset,
        IAsyncEnumerable<VideoStreamFrameDto[]> videoStream)
    {
        // AY: No CancellationToken argument here, otherwise SignalR binder fails!
        Log.LogInformation("PushVideo: Started with codec={Codec}, {Width}x{Height}, codecSettings={CodecSettingsLen} chars",
            codec, width, height, codecSettings?.Length ?? 0);

        // Debug wrapper to count incoming batches
        async IAsyncEnumerable<VideoStreamFrameDto> LogBatches(IAsyncEnumerable<VideoStreamFrameDto[]> source)
        {
            var batchCount = 0;
            var frameCount = 0;
            await foreach (var batch in source) {
                batchCount++;
                if (batch == null) {
                    Log.LogWarning("PushVideo: received null batch #{BatchCount}", batchCount);
                    continue;
                }
                Log.LogInformation("PushVideo: received batch #{BatchCount} with {FrameCount} frames", batchCount, batch.Length);
                foreach (var dto in batch) {
                    frameCount++;
                    if (frameCount <= 3 || frameCount % 30 == 0 || dto.IsKeyFrame)
                        Log.LogInformation("PushVideo frame #{Count}: Offset={Offset}, IsKey={IsKey}, DataLen={DataLen}, DescLen={DescLen}",
                            frameCount, dto.Offset, dto.IsKeyFrame, dto.Data?.Length ?? 0, dto.Description?.Length ?? 0);
                    yield return dto;
                }
            }
            Log.LogInformation("PushVideo: stream ended after {BatchCount} batches, {FrameCount} total frames", batchCount, frameCount);
        }

        return PushVideo(
            sessionToken,
            chatId,
            codec,
            width,
            height,
            codecSettings,
            clientStartOffset,
            LogBatches(videoStream).Select(dto => dto.ToVideoFrame()));
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

    private async Task PushVideo(
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

        // Debug: wrap stream to count and log frames
        var frameCount = 0;
        async IAsyncEnumerable<VideoFrame> LogFrames(IAsyncEnumerable<VideoFrame> source)
        {
            await foreach (var frame in source) {
                frameCount++;
                if (frameCount <= 3 || frameCount % 30 == 0)
                    Log.LogInformation("PushVideo frame #{Count}: Offset={Offset}ms, Duration={Duration}ms, IsKey={IsKey}, Size={Size}, W={W}, H={H}",
                        frameCount, frame.Offset.TotalMilliseconds, frame.Duration.TotalMilliseconds,
                        frame.IsKeyFrame, frame.Data?.Length ?? 0, frame.Width, frame.Height);
                yield return frame;
            }
            Log.LogInformation("PushVideo stream ended after {Count} frames", frameCount);
        }

        var frames = LogFrames(videoStream.SuppressCancellation(stopCts.Token));
        var frameStream = RpcStream.New(frames);
        await Backend
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
}
