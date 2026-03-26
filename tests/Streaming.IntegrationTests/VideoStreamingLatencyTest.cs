using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using ActualChat.Chat;
using ActualChat.Security;
using ActualChat.Testing.Host;
using ActualChat.Video;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
[Trait("Category", "Slow")]
public class VideoStreamingLatencyTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const int TotalFrames = 300; // 10 seconds at 30fps
    private const int GopSize = 30; // 1 keyframe per second
    private const int KeyFrameDataSize = 40_000; // 40KB
    private const int DeltaFrameDataSize = 10_000; // 10KB
    private const int FrameWidth = 1280;
    private const int FrameHeight = 720;
    private const string Codec = "avc1";
    private static readonly TimeSpan FrameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 30);

    [Fact/*(Skip="Manual")*/]
    public async Task ShouldDeliverVideoFramesWithAcceptableLatency()
    {
        // Arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("VideoTestUser"));

        var chatId = await CreateTestChat(session);
        var sessionToken = await CreateSessionToken(session);
        var hubUrl = services.UrlMapper().ToAbsolute("/api/hub/streams");

        var sentTimestamps = new ConcurrentDictionary<long, long>();

        // Pre-create consumer connection in parallel with ObserveStreams to reduce first-frame latency
        await using var consumerConnection = CreateHubConnection(hubUrl);
        var consumerConnectTask = consumerConnection.StartAsync();

        // Start ObserveStreams BEFORE producer — consumer is already watching when stream appears
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var observeTask = ObserveNewStream(chatId, observeCts.Token);

        // Start producer
        await using var producerConnection = CreateHubConnection(hubUrl);
        await producerConnection.StartAsync();

        var streamCreationTs = Stopwatch.GetTimestamp();
        var clientStartOffset = CpuClock.Instance.Now.EpochOffset.TotalSeconds;
        var pushTask = producerConnection.SendAsync("PushVideo",
            sessionToken, chatId.Value, Codec, FrameWidth, FrameHeight, "",
            clientStartOffset,
            PushFramesAsync(TotalFrames, sentTimestamps));

        // ObserveStreams yields VideoStreamInfo when stream is registered
        var streamInfo = await observeTask;
        var streamDiscoveredTs = Stopwatch.GetTimestamp();
        var discoveryLatency = Stopwatch.GetElapsedTime(streamCreationTs, streamDiscoveredTs);
        Out.WriteLine($"Discovered stream via ObserveStreams: {streamInfo.StreamId}");
        Out.WriteLine($"Stream creation → ObserveStreams notification: {discoveryLatency.TotalMilliseconds:F1}ms");

        // Ensure consumer connection is ready
        await consumerConnectTask;

        var keyframeLatencies = new List<double>();
        var receivedOffsets = new List<long>();
        VideoFrame? firstFrame = null;
        long firstFrameTs = 0;
        var firstKeyframeSkipped = false;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = consumerConnection.StreamAsync<byte[]>(
            "GetVideo", sessionToken, streamInfo.StreamId.Value, 0.0, cts.Token);

        // Read frames — handle stream disconnections gracefully (server may close
        // the WebSocket when the producer stream ends or during RPC reconnection)
        var streamEndedCleanly = true;
        try {
            await foreach (var frameBytes in stream) {
                var receiveTs = Stopwatch.GetTimestamp();
                var frame = DeserializeVideoFrame(frameBytes);
                if (frame == null)
                    continue;

                if (firstFrame == null) {
                    firstFrame = frame;
                    firstFrameTs = receiveTs;
                }

                receivedOffsets.Add(frame.Offset.Ticks);

                // Track latency for keyframes — skip the first keyframe which includes
                // the full discovery + consumer-connect overhead
                if (frame.IsKeyFrame && sentTimestamps.TryGetValue(frame.Offset.Ticks, out var sentTs)) {
                    var latency = Stopwatch.GetElapsedTime(sentTs, receiveTs);
                    if (!firstKeyframeSkipped)
                        firstKeyframeSkipped = true;
                    else
                        keyframeLatencies.Add(latency.TotalMilliseconds);
                    Out.WriteLine($"  Keyframe @ {frame.Offset.TotalSeconds:F2}s: latency={latency.TotalMilliseconds:F1}ms");
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) {
            streamEndedCleanly = false;
            Out.WriteLine($"Consumer stream disconnected ({e.GetType().Name}): {e.Message}");
        }

        // Wait for producer to finish — don't fail if it errors after consumer disconnect
        try { await pushTask; }
        catch (Exception e) {
            Out.WriteLine($"Producer ended with {e.GetType().Name}: {e.Message}");
        }

        // Assert & report
        firstFrame.Should().NotBeNull("should receive at least one frame");
        firstFrame!.IsKeyFrame.Should().BeTrue("first received frame must be a keyframe");

        var totalFirstFrameLatency = Stopwatch.GetElapsedTime(streamCreationTs, firstFrameTs);
        var discoveryToFirstFrame = Stopwatch.GetElapsedTime(streamDiscoveredTs, firstFrameTs);

        Out.WriteLine("");
        Out.WriteLine("=== Latency Summary ===");
        Out.WriteLine($"Stream creation → ObserveStreams notification: {discoveryLatency.TotalMilliseconds:F1}ms");
        Out.WriteLine($"ObserveStreams → GetVideo first frame: {discoveryToFirstFrame.TotalMilliseconds:F1}ms");
        Out.WriteLine($"Total stream-creation-to-first-frame: {totalFirstFrameLatency.TotalMilliseconds:F1}ms");
        Out.WriteLine($"Stream ended cleanly: {streamEndedCleanly}");

        if (keyframeLatencies.Count > 0) {
            var p50 = Percentile(keyframeLatencies, 0.50);
            var p95 = Percentile(keyframeLatencies, 0.95);
            var p99 = Percentile(keyframeLatencies, 0.99);
            Out.WriteLine($"Keyframe latencies (excluding first): p50={p50:F1}ms, p95={p95:F1}ms, p99={p99:F1}ms");
        }

        var deliveryRatio = (double)receivedOffsets.Count / TotalFrames;
        Out.WriteLine($"Frame delivery: {receivedOffsets.Count}/{TotalFrames} ({deliveryRatio:P1})");

        // Discovery relies on invalidation now
        discoveryLatency.TotalMilliseconds.Should().BeLessThan(2000,
            "stream discovery via ObserveStreams should be under 2000ms");

        totalFirstFrameLatency.TotalMilliseconds.Should().BeLessThan(1500,
            "total stream-creation-to-first-frame should be under 1500ms");

        if (keyframeLatencies.Count > 0)
            Percentile(keyframeLatencies, 0.95).Should().BeLessThan(500,
                "p95 keyframe latency should be under 500ms");

        // Verify monotonic offset ordering
        for (var i = 1; i < receivedOffsets.Count; i++)
            receivedOffsets[i].Should().BeGreaterThan(receivedOffsets[i - 1],
                $"frame offsets should be monotonically increasing (index {i})");

        // Verify frame delivery — only assert high delivery if stream ended cleanly
        if (streamEndedCleanly)
            deliveryRatio.Should().BeGreaterThanOrEqualTo(0.8, "at least 80% of frames should be delivered");
        else
            receivedOffsets.Count.Should().BeGreaterThanOrEqualTo(10,
                "should receive at least 10 frames even with early stream termination");
    }

    [Fact/*(Skip="Manual")*/]
    public async Task ShouldStartFromKeyFrameWhenJoiningLate()
    {
        // Arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("VideoLateJoinUser"));

        var chatId = await CreateTestChat(session);
        var sessionToken = await CreateSessionToken(session);
        var hubUrl = services.UrlMapper().ToAbsolute("/api/hub/streams");

        var sentTimestamps = new ConcurrentDictionary<long, long>();

        // Start producer first — stream must exist before consumer joins
        await using var producerConnection = CreateHubConnection(hubUrl);
        await producerConnection.StartAsync();

        var clientStartOffset = CpuClock.Instance.Now.EpochOffset.TotalSeconds;

        var pushTask = producerConnection.SendAsync("PushVideo",
            sessionToken, chatId.Value, Codec, FrameWidth, FrameHeight, "",
            clientStartOffset,
            PushFramesAsync(TotalFrames, sentTimestamps));

        // Discover stream via ObserveStreams (yields when stream is registered)
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var observeStartTs = Stopwatch.GetTimestamp();
        var streamInfo = await ObserveNewStream(chatId, observeCts.Token);
        var observeLatency = Stopwatch.GetElapsedTime(observeStartTs, Stopwatch.GetTimestamp());
        Out.WriteLine($"ObserveStreams yielded stream: {streamInfo.StreamId} (in {observeLatency.TotalMilliseconds:F1}ms)");

        // Wait ~5 seconds for producer data to accumulate in the retention buffer.
        // Note: SendAsync is fire-and-forget — pushTask completes when the initial
        // invocation message is sent, NOT when all stream items are delivered.
        // The IAsyncEnumerable is consumed in a background task by SignalR.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Out.WriteLine($"Waited ~5s for producer data ({sentTimestamps.Count} frames sent so far), joining late...");

        // Late consumer joins
        await using var consumerConnection = CreateHubConnection(hubUrl);
        await consumerConnection.StartAsync();

        var keyframeLatencies = new List<double>();
        var receivedOffsets = new List<long>();
        VideoFrame? firstFrame = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = consumerConnection.StreamAsync<byte[]>(
            "GetVideo", sessionToken, streamInfo.StreamId.Value, 0.0, cts.Token);

        var streamEndedCleanly = true;
        try {
            await foreach (var frameBytes in stream) {
                var receiveTs = Stopwatch.GetTimestamp();
                var frame = DeserializeVideoFrame(frameBytes);
                if (frame == null)
                    continue;

                firstFrame ??= frame;
                receivedOffsets.Add(frame.Offset.Ticks);

                // Track keyframe latencies
                if (frame.IsKeyFrame && sentTimestamps.TryGetValue(frame.Offset.Ticks, out var sentTs)) {
                    var latency = Stopwatch.GetElapsedTime(sentTs, receiveTs);
                    keyframeLatencies.Add(latency.TotalMilliseconds);
                    Out.WriteLine($"  Keyframe @ {frame.Offset.TotalSeconds:F2}s: latency={latency.TotalMilliseconds:F1}ms");
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) {
            streamEndedCleanly = false;
            Out.WriteLine($"Consumer stream disconnected ({e.GetType().Name}): {e.Message}");
        }

        try { await pushTask; }
        catch (Exception e) {
            Out.WriteLine($"Producer ended with {e.GetType().Name}: {e.Message}");
        }

        // Assert & report
        firstFrame.Should().NotBeNull("should receive at least one frame");
        firstFrame!.IsKeyFrame.Should().BeTrue("first received frame must be a keyframe (SkipToLatestBufferedKeyFrame)");

        Out.WriteLine("");
        Out.WriteLine("=== Late Join Summary ===");
        Out.WriteLine($"First frame offset: {firstFrame.Offset.TotalSeconds:F2}s, IsKeyFrame: {firstFrame.IsKeyFrame}");
        Out.WriteLine($"Stream ended cleanly: {streamEndedCleanly}");

        firstFrame.Offset.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3),
            "late joiner should skip ahead to recent keyframe in the retention buffer");

        if (keyframeLatencies.Count > 0) {
            var p50 = Percentile(keyframeLatencies, 0.50);
            var p95 = Percentile(keyframeLatencies, 0.95);
            var p99 = Percentile(keyframeLatencies, 0.99);
            Out.WriteLine($"Keyframe latencies: p50={p50:F1}ms, p95={p95:F1}ms, p99={p99:F1}ms");

            p95.Should().BeLessThan(500, "p95 keyframe latency for late-join frames should be under 500ms");
        }

        var deliveryRatio = receivedOffsets.Count > 0
            ? (double)receivedOffsets.Count / (TotalFrames - firstFrame.Offset.Ticks / FrameDuration.Ticks)
            : 0;
        Out.WriteLine($"Frame delivery (from join point): {receivedOffsets.Count} frames ({deliveryRatio:P1})");
    }

    /// <summary>
    /// Tests client-driven skip-to-live: when GetVideo is called with the sentinel skipTo value,
    /// the server skips all buffered/delta frames and starts from the next keyframe at the live edge.
    /// </summary>
    [Fact]
    public async Task ShouldSkipToLiveWhenClientReRequestsStream()
    {
        // Arrange
        const int totalFrames = 600; // 20 seconds at 30fps
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("VideoSkipToLiveUser"));

        var chatId = await CreateTestChat(session);
        var sessionToken = await CreateSessionToken(session);
        var hubUrl = services.UrlMapper().ToAbsolute("/api/hub/streams");

        var sentTimestamps = new ConcurrentDictionary<long, long>();

        // Start producer
        await using var producerConnection = CreateHubConnection(hubUrl);
        await producerConnection.StartAsync();

        var clientStartOffset = CpuClock.Instance.Now.EpochOffset.TotalSeconds;
        var pushTask = producerConnection.SendAsync("PushVideo",
            sessionToken, chatId.Value, Codec, FrameWidth, FrameHeight, "",
            clientStartOffset,
            PushFramesAsync(totalFrames, sentTimestamps));

        // Discover stream
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var streamInfo = await ObserveNewStream(chatId, observeCts.Token);
        Out.WriteLine($"Discovered stream: {streamInfo.StreamId}");

        // Wait for ~5 seconds of frames to be produced (150 frames at 30fps)
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Now call GetVideo with the skip-to-live sentinel value
        // This should skip all buffered frames and start from the next keyframe
        await using var consumerConnection = CreateHubConnection(hubUrl);
        await consumerConnection.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = consumerConnection.StreamAsync<byte[]>(
            "GetVideo", sessionToken, streamInfo.StreamId.Value, 0.0, cts.Token);

        // Read the first frame — it should be a keyframe near the live edge
        VideoFrame? firstFrame = null;
        try {
            await foreach (var frameBytes in stream) {
                firstFrame = DeserializeVideoFrame(frameBytes);
                if (firstFrame != null)
                    break;
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) {
            Out.WriteLine($"Consumer stream disconnected ({e.GetType().Name}): {e.Message}");
        }

        // Wait for producer to finish
        try { await pushTask; }
        catch (Exception e) {
            Out.WriteLine($"Producer ended with {e.GetType().Name}: {e.Message}");
        }

        // Assertions
        firstFrame.Should().NotBeNull("should receive at least one frame after skip-to-live");
        firstFrame!.IsKeyFrame.Should().BeTrue("first frame after skip-to-live should be a keyframe");

        // The keyframe should be near the live edge (offset close to wall-clock elapsed time)
        var elapsedSinceStart = CpuClock.Instance.Now.EpochOffset.TotalSeconds - clientStartOffset;
        Out.WriteLine($"First frame: offset={firstFrame.Offset.TotalSeconds:F2}s, isKeyFrame={firstFrame.IsKeyFrame}, elapsed={elapsedSinceStart:F2}s");

        // The frame offset should be at least 3 seconds into the stream (we waited 5s)
        firstFrame.Offset.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3),
            "keyframe should be near the live edge, not at the start of the buffer");

        Out.WriteLine($"SkipToLive successful: first frame at {firstFrame.Offset.TotalSeconds:F2}s");
    }

    // Helper methods

    private async Task<VideoStreamInfo> ObserveNewStream(ChatId chatId, CancellationToken ct)
    {
        var liveVideoBackend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        // Watch computed List for a stream to appear
        while (true) {
            var computed = await Computed.Capture(() => liveVideoBackend.List(chatId, ct), ct);
            if (computed.Value.Count > 0)
                return computed.Value[0]; // first stream found
            await computed.WhenInvalidated(ct);
        }
    }

    private HubConnection CreateHubConnection(string hubUrl)
        => new HubConnectionBuilder()
            .WithUrl(hubUrl, o => {
                o.SkipNegotiation = true;
                o.Transports = HttpTransportType.WebSockets;
            })
            .AddMessagePackProtocol()
            .Build();

    private async Task<string> CreateSessionToken(Session session)
    {
        var secureTokensBackend = AppHost.Services.GetRequiredService<ISecureTokensBackend>();
        var secureToken = await secureTokensBackend.Create(session.Id);
        return secureToken.Token;
    }

    private async Task<ChatId> CreateTestChat(Session session)
    {
        var chat = await Commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "VideoLatencyTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        return chat.Id;
    }

    private static byte[] SerializeVideoFrame(VideoFrame frame)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);

        var fieldCount = 3; // offset, duration, data — always present
        if (frame.IsKeyFrame) fieldCount += 3; // isKeyFrame, width, height
        if (frame.Description != null) fieldCount++;
        if (frame.Codec != null) fieldCount++;

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

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static VideoFrame? DeserializeVideoFrame(byte[] bytes)
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
                Codec = codec,
            };
        }
        catch {
            return null;
        }
    }

    private static VideoFrame GenerateFrame(int index)
    {
        var isKeyFrame = index % GopSize == 0;
        var dataSize = isKeyFrame ? KeyFrameDataSize : DeltaFrameDataSize;
        var data = new byte[dataSize];
        // Fill with deterministic pattern for debugging
        data[0] = (byte)(index & 0xFF);
        data[1] = (byte)((index >> 8) & 0xFF);

        var offset = TimeSpan.FromTicks(FrameDuration.Ticks * index);

        return new VideoFrame(isKeyFrame) {
            Data = data,
            Offset = offset,
            Duration = FrameDuration,
            Width = isKeyFrame ? FrameWidth : 0,
            Height = isKeyFrame ? FrameHeight : 0,
            Description = isKeyFrame ? new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 } : null, // Fake SPS NAL
            Codec = isKeyFrame ? Codec : null,
        };
    }

    private async IAsyncEnumerable<byte[]> PushFramesAsync(
        int totalFrames,
        ConcurrentDictionary<long, long> sentTimestamps,
        int halfwaySignalAtFrame = -1,
        TaskCompletionSource? halfwayTcs = null)
    {
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < totalFrames; i++) {
            var frame = GenerateFrame(i);
            var serialized = SerializeVideoFrame(frame);

            // Drift-compensated real-time pacing
            var targetElapsed = TimeSpan.FromTicks(FrameDuration.Ticks * i);
            var remaining = targetElapsed - sw.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);

            sentTimestamps[frame.Offset.Ticks] = Stopwatch.GetTimestamp();
            yield return serialized;

            if (halfwaySignalAtFrame >= 0 && i == halfwaySignalAtFrame)
                halfwayTcs?.TrySetResult();
        }
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}
