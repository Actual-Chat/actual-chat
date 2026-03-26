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

    [Fact(Skip="Manual")]
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

        // Assertions — ObserveStreams goes through RPC infrastructure, so discovery
        // includes RPC setup + producer WebSocket connect + stream registration
        discoveryLatency.TotalMilliseconds.Should().BeLessThan(500,
            "stream discovery via ObserveStreams should be under 500ms");

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

    [Fact(Skip="Manual")]
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

    [Fact(Skip="Manual")]
    public async Task ShouldSkipToLiveWhenLatencyExceedsThreshold()
    {
        // Arrange
        const int skipToLiveTotalFrames = 600; // 20 seconds at 30fps
        const int normalReadFrames = 360; // ~12 seconds
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("VideoSkipToLiveUser"));

        var chatId = await CreateTestChat(session);
        var sessionToken = await CreateSessionToken(session);
        var hubUrl = services.UrlMapper().ToAbsolute("/api/hub/streams");

        var sentTimestamps = new ConcurrentDictionary<long, long>();

        // Start producer — push 600 frames (20s at 30fps)
        await using var producerConnection = CreateHubConnection(hubUrl);
        await producerConnection.StartAsync();

        var clientStartOffset = CpuClock.Instance.Now.EpochOffset.TotalSeconds;
        var pushTask = producerConnection.SendAsync("PushVideo",
            sessionToken, chatId.Value, Codec, FrameWidth, FrameHeight, "",
            clientStartOffset,
            PushFramesAsync(skipToLiveTotalFrames, sentTimestamps));

        // Discover stream
        using var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var streamInfo = await ObserveNewStream(chatId, observeCts.Token);
        Out.WriteLine($"Discovered stream: {streamInfo.StreamId}");

        // Start consumer
        await using var consumerConnection = CreateHubConnection(hubUrl);
        await consumerConnection.StartAsync();

        var receivedOffsets = new List<long>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long lastPreGateOffsetTicks = 0;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stream = consumerConnection.StreamAsync<byte[]>(
            "GetVideo", sessionToken, streamInfo.StreamId.Value, 0.0, cts.Token);

        // Trigger task — runs concurrently with consumer reading
        var triggerTask = Task.Run(async () => {
            // Wait for consumer to reach the gate (~12s of reading)
            await gate.Task;
            Out.WriteLine($"Consumer paused at frame {normalReadFrames}, letting frames buffer...");

            // Wait ~3s more so frames accumulate while consumer is paused
            await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

            // Report latency with streamOffsetMs=0 — backend computes latency ≈ elapsed >> 3000ms
            Out.WriteLine("Reporting high latency (streamOffsetMs=0) to trigger SkipToLive...");
            await consumerConnection.InvokeAsync(
                "ReportVideoLatency", sessionToken, streamInfo.StreamId.Value, 0.0, cts.Token);

            // Wait for flag propagation through the pipeline
            await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
            Out.WriteLine("SkipToLive triggered, resuming consumer...");
        }, cts.Token);

        // Phase 1: Read frames normally, pause after normalReadFrames
        var frameCount = 0;
        var skipDetected = false;
        long firstPostSkipOffsetTicks = 0;
        var firstPostSkipIsKeyFrame = false;
        var streamEndedCleanly = true;

        try {
            await foreach (var frameBytes in stream) {
                var frame = DeserializeVideoFrame(frameBytes);
                if (frame == null)
                    continue;

                frameCount++;
                receivedOffsets.Add(frame.Offset.Ticks);

                if (frameCount <= normalReadFrames) {
                    // Phase 1: normal reading
                    lastPreGateOffsetTicks = frame.Offset.Ticks;

                    if (frameCount == normalReadFrames) {
                        Out.WriteLine($"Phase 1 complete: read {frameCount} frames, last offset={new TimeSpan(lastPreGateOffsetTicks).TotalSeconds:F2}s");
                        gate.TrySetResult();

                        // Wait for trigger task to complete before resuming
                        await triggerTask;
                    }
                }
                else if (!skipDetected) {
                    // First frame after gate — check for skip
                    firstPostSkipOffsetTicks = frame.Offset.Ticks;
                    firstPostSkipIsKeyFrame = frame.IsKeyFrame;
                    skipDetected = true;

                    var gapSeconds = (firstPostSkipOffsetTicks - lastPreGateOffsetTicks) / (double)TimeSpan.TicksPerSecond;
                    Out.WriteLine($"Phase 2: first frame after skip at offset={frame.Offset.TotalSeconds:F2}s, gap={gapSeconds:F2}s, isKeyFrame={frame.IsKeyFrame}");

                    // We've detected the skip — no need to read the rest
                    break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) {
            streamEndedCleanly = false;
            Out.WriteLine($"Consumer stream disconnected ({e.GetType().Name}): {e.Message}");
        }

        // Wait for producer to finish
        try { await pushTask; }
        catch (Exception e) {
            Out.WriteLine($"Producer ended with {e.GetType().Name}: {e.Message}");
        }

        // Assertions
        skipDetected.Should().BeTrue("consumer should have received frames after the skip");

        firstPostSkipIsKeyFrame.Should().BeTrue(
            "first frame after SkipToLive should be a keyframe");

        var offsetGap = TimeSpan.FromTicks(firstPostSkipOffsetTicks - lastPreGateOffsetTicks);
        Out.WriteLine($"Offset gap: {offsetGap.TotalSeconds:F2}s ({offsetGap.Ticks / FrameDuration.Ticks} frames)");

        offsetGap.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2),
            "SkipToLive should jump past buffered frames (≥2s gap expected)");

        Out.WriteLine($"Total frames read: {receivedOffsets.Count}");
    }

    // Helper methods

    private async Task<VideoStreamInfo> ObserveNewStream(ChatId chatId, CancellationToken ct)
    {
        var liveVideoBackend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var rpcStream = await liveVideoBackend.Observe(chatId, ct);
        await foreach (var streamInfo in rpcStream.WithCancellation(ct))
            return streamInfo; // first stream observed
        throw new OperationCanceledException("ObserveStreams completed without yielding a stream");
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
