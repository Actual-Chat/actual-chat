using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using ActualChat;
using ActualChat.Chat;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Security;
using ActualChat.Streaming;
using ActualChat.Users;
using ActualChat.Video;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static System.Console;

CoreSerializerAndRpcSetup.Configure(true);

// --- Configuration ---
const int GopSize = 30;
const int KeyFrameDataSize = 40_000;
const int DeltaFrameDataSize = 10_000;
const int FrameWidth = 1280;
const int FrameHeight = 720;
const string Codec = "avc1";
var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 30);

var chatCount = int.TryParse(GetArg("c", "chats"), out var cc) ? cc : 10;
var streamsPerChat = int.TryParse(GetArg("s", "streams"), out var sc) ? sc : 6;
var consumersPerChat = int.TryParse(GetArg("n", "consumers"), out var nc) ? nc : 6;
var baseUrl = GetArg("u", "url") ?? "https://local.voxt.ai";
var durationSec = int.TryParse(GetArg("d", "duration"), out var dd) ? dd : 30;
var testDuration = TimeSpan.FromSeconds(durationSec);

// Each consumer pulls all streams except its own → (consumersPerChat) × (streamsPerChat - 1) pulls per chat
// But consumers may not map 1:1 to producers, so each consumer pulls all streams in its chat
var pullsPerChat = consumersPerChat * (streamsPerChat - 1);
var totalPulls = chatCount * pullsPerChat;
var totalStreams = chatCount * streamsPerChat;

WriteLine($"Video Load Test: {chatCount} chats × {streamsPerChat} streams × {consumersPerChat} consumers");
WriteLine($"  {totalStreams} total streams, {totalPulls} total pulls ({pullsPerChat} per chat)");
WriteLine($"  Base URL: {baseUrl}, Duration: {durationSec}s");

// --- Setup ---
var cts = new CancellationTokenSource();
CancelKeyPress += (_, args) => { args.Cancel = true; cts.Cancel(); };

var services = CreateServiceProvider(baseUrl);
var commander = services.Commander();
var session = Session.New();
var hubUrl = $"{baseUrl}/api/hub/streams";

// Authenticate
WriteLine("Signing in...");
var signedIn = await commander.Call(
    new EmailAuth_ValidateTotp(session, Email.New("test-videoload@actual.chat"), 111111), cts.Token);
if (!signedIn)
    throw new InvalidOperationException("Sign-in failed. Is the server running with test agent bypass?");
WriteLine("Signed in.");

// Get session token for SignalR
var secureTokens = services.GetRequiredService<ISecureTokens>();
var secureToken = await secureTokens.CreateForSession(session, cts.Token);
var sessionToken = secureToken.Token;

// Create chats
WriteLine($"Creating {chatCount} test chats...");
// var chatIds = new ChatId[chatCount];
// for (var i = 0; i < chatCount; i++) {
//     var chat = await commander.Call(new Chats_Change(session, null, null, new() {
//         Create = new ChatDiff {
//             Title = $"VideoLoadTest-{i}",
//             Kind = ChatKind.Group,
//             IsPublic = true,
//         },
//     }), cts.Token);
//     chat.Require();
//     chatIds[i] = chat.Id;
// }
var chatIds = new ChatId[] {
    ChatId.Parse("zqMxFSJWkS"),
    ChatId.Parse("weFGfFJNgy"),
    ChatId.Parse("uKWaKUGZmv"),
    ChatId.Parse("xPUTIYhnMJ"),
    ChatId.Parse("Q5wNxJIeVD"),
    ChatId.Parse("3USxtLtliz"),
    ChatId.Parse("pzOCDCChRR"),
    ChatId.Parse("D0S4FShrsu"),
    ChatId.Parse("mMOf4Lj0gw"),
    ChatId.Parse("NcnBmEfc5e"),
};
WriteLine($"Created {chatCount} chats.");

// --- Metrics ---
// Key: (chatIndex, producerIndex, offsetTicks) → sendTimestamp
var sentTimestamps = new ConcurrentDictionary<(int Chat, int Producer, long Offset), long>();
// Key: (chatIndex, consumerIndex, streamIndex) → metrics
var latencies = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), ConcurrentBag<double>>();
var framesReceived = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), int>();
var bytesReceived = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), long>();

// --- Start all producers across all chats ---
WriteLine($"Starting {totalStreams} producers...");
var producerTasks = new List<Task>();
for (var ci = 0; ci < chatCount; ci++) {
    for (var pi = 0; pi < streamsPerChat; pi++) {
        var chatIdx = ci;
        var prodIdx = pi;
        producerTasks.Add(Task.Run(() => RunProducer(chatIdx, prodIdx, cts.Token), cts.Token));
    }
}

// --- Discover streams per chat ---
WriteLine("Waiting for streams to appear...");
var liveVideoStreams = services.GetRequiredService<ILiveVideoStreams>();
var discoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, discoveryTimeout.Token);

var chatStreams = new ApiArray<VideoStreamInfo>[chatCount];
var discoveryTasks = new Task[chatCount];
for (var ci = 0; ci < chatCount; ci++) {
    var chatIdx = ci;
    discoveryTasks[ci] = Task.Run(async () => {
        var cid = chatIds[chatIdx];
        while (true) {
            var computed = await Computed.Capture(
                () => liveVideoStreams.List(session, cid, linkedCts.Token), linkedCts.Token);
            if (computed.Value.Count >= streamsPerChat) {
                chatStreams[chatIdx] = computed.Value;
                return;
            }
            await computed.WhenInvalidated(linkedCts.Token);
        }
    }, linkedCts.Token);
}
await Task.WhenAll(discoveryTasks);
WriteLine($"All {totalStreams} streams discovered across {chatCount} chats.");

// --- Start consumers ---
WriteLine($"Starting {totalPulls} consumer pulls...");
var consumerTasks = new List<Task>();
for (var ci = 0; ci < chatCount; ci++) {
    var streams = chatStreams[ci];
    for (var consIdx = 0; consIdx < consumersPerChat; consIdx++) {
        for (var si = 0; si < streams.Count; si++) {
            // Skip own stream: consumer N skips stream N
            if (si == consIdx) continue;

            var chatIdx = ci;
            var consumerIdx = consIdx;
            var streamIdx = si;
            var streamId = streams[si].StreamId;
            latencies[(chatIdx, consumerIdx, streamIdx)] = new ConcurrentBag<double>();
            framesReceived[(chatIdx, consumerIdx, streamIdx)] = 0;
            bytesReceived[(chatIdx, consumerIdx, streamIdx)] = 0;
            consumerTasks.Add(Task.Run(
                () => RunConsumer(chatIdx, consumerIdx, streamIdx, streamId, cts.Token), cts.Token));
        }
    }
}

// --- Wait for test duration ---
WriteLine($"Running for {durationSec}s... (Ctrl+C to stop early)");
try { await Task.Delay(testDuration, cts.Token); }
catch (OperationCanceledException) { }

WriteLine("Stopping...");
await cts.CancelAsync();

await Task.WhenAll(
    producerTasks.Concat(consumerTasks).Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));

// --- Report ---
PrintReport();

// ============================================
// Helper methods
// ============================================

async Task RunProducer(int chatIdx, int prodIdx, CancellationToken ct)
{
    try {
        await using var connection = CreateHubConnection(hubUrl);
        await connection.StartAsync(ct);
        var clientStartOffset = CpuClock.Instance.Now.EpochOffset.TotalSeconds;

        await connection.SendAsync("PushVideo",
            sessionToken, chatIds[chatIdx].Value, Codec, FrameWidth, FrameHeight, "",
            clientStartOffset, 0, // 0 = Webcam
            PushFrames(chatIdx, prodIdx, ct), ct);

        try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct); }
        catch (OperationCanceledException) { }
    }
    catch (Exception e) when (e is not OperationCanceledException) {
        Error.WriteLine($"Producer[chat={chatIdx},prod={prodIdx}] error: {e.GetType().Name}: {e.Message}");
    }
}

async IAsyncEnumerable<byte[]> PushFrames(
    int chatIdx, int prodIdx,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    for (var i = 0; !ct.IsCancellationRequested; i++) {
        var frame = GenerateFrame(i);
        var serialized = SerializeVideoFrame(frame);

        var targetElapsed = TimeSpan.FromTicks(frameDuration.Ticks * i);
        var remaining = targetElapsed - sw.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        sentTimestamps[(chatIdx, prodIdx, frame.Offset.Ticks)] = Stopwatch.GetTimestamp();
        yield return serialized;
    }
}

async Task RunConsumer(int chatIdx, int consumerIdx, int streamIdx, StreamId streamId, CancellationToken ct)
{
    try {
        await using var connection = CreateHubConnection(hubUrl);
        await connection.StartAsync(ct);

        var stream = connection.StreamAsync<byte[]>("GetVideo", sessionToken, streamId.Value, 0.0, ct);
        await foreach (var frameBytes in stream) {
            if (ct.IsCancellationRequested) break;

            var receiveTs = Stopwatch.GetTimestamp();
            var frame = DeserializeVideoFrame(frameBytes);
            if (frame == null) continue;

            var key = (chatIdx, consumerIdx, streamIdx);
            framesReceived.AddOrUpdate(key, 1, (_, v) => v + 1);
            bytesReceived.AddOrUpdate(key, frameBytes.Length, (_, v) => v + frameBytes.Length);

            if (sentTimestamps.TryGetValue((chatIdx, streamIdx, frame.Offset.Ticks), out var sentTs)) {
                var latencyMs = Stopwatch.GetElapsedTime(sentTs, receiveTs).TotalMilliseconds;
                latencies[key].Add(latencyMs);
            }
        }
    }
    catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException) {
        // Expected on shutdown
    }
    catch (Exception e) {
        Error.WriteLine($"Consumer[chat={chatIdx},cons={consumerIdx},stream={streamIdx}] error: {e.GetType().Name}: {e.Message}");
    }
}

void PrintReport()
{
    WriteLine();
    WriteLine("=== VIDEO LOAD TEST RESULTS ===");
    WriteLine($"Duration: {durationSec}s, Chats: {chatCount}, Streams/chat: {streamsPerChat}, Consumers/chat: {consumersPerChat}");
    WriteLine($"Total streams: {totalStreams}, Total pulls: {totalPulls}");
    WriteLine();

    // Per-chat summary
    WriteLine("--- Per-Chat Summary ---");
    WriteLine($"{"Chat",-6} {"Frames",-10} {"MB/s",-8} {"p50ms",-8} {"p95ms",-8} {"p99ms",-8}");
    for (var ci = 0; ci < chatCount; ci++) {
        var chatLatencies = new List<double>();
        var chatFrames = 0;
        long chatBytes = 0;
        for (var consIdx = 0; consIdx < consumersPerChat; consIdx++) {
            for (var si = 0; si < streamsPerChat; si++) {
                if (si == consIdx) continue;
                chatFrames += framesReceived.GetValueOrDefault((ci, consIdx, si));
                chatBytes += bytesReceived.GetValueOrDefault((ci, consIdx, si));
                if (latencies.TryGetValue((ci, consIdx, si), out var bag))
                    chatLatencies.AddRange(bag);
            }
        }
        var mbps = chatBytes / (1024.0 * 1024.0) / durationSec;
        WriteLine($"{ci,-6} {chatFrames,-10} {mbps,-8:F2} " +
                  $"{Percentile(chatLatencies, 0.50),-8:F1} " +
                  $"{Percentile(chatLatencies, 0.95),-8:F1} " +
                  $"{Percentile(chatLatencies, 0.99),-8:F1}");
    }

    // Aggregate
    var aggLatencies = new List<double>();
    var aggFrames = 0;
    long aggBytes = 0;
    foreach (var kv in framesReceived) aggFrames += kv.Value;
    foreach (var kv in bytesReceived) aggBytes += kv.Value;
    foreach (var kv in latencies) aggLatencies.AddRange(kv.Value);

    var aggMbps = aggBytes / (1024.0 * 1024.0) / durationSec;
    WriteLine();
    WriteLine("--- Aggregate ---");
    WriteLine($"Total frames received: {aggFrames}");
    WriteLine($"Total bytes: {aggBytes:N0} ({aggMbps:F2} MB/s)");
    if (aggLatencies.Count > 0) {
        WriteLine($"Latency p50={Percentile(aggLatencies, 0.50):F1}ms, " +
                  $"p95={Percentile(aggLatencies, 0.95):F1}ms, " +
                  $"p99={Percentile(aggLatencies, 0.99):F1}ms");
    }
    var expectedFramesPerPull = (int)(durationSec * 30);
    var expectedTotal = expectedFramesPerPull * totalPulls;
    WriteLine($"Expected ~{expectedTotal} total frames, got {aggFrames} ({100.0 * aggFrames / Math.Max(1, expectedTotal):F1}%)");
}

// ============================================
// Frame generation & serialization (from VideoStreamingLatencyTest)
// ============================================

VideoFrame GenerateFrame(int index)
{
    var isKeyFrame = index % GopSize == 0;
    var dataSize = isKeyFrame ? KeyFrameDataSize : DeltaFrameDataSize;
    var data = new byte[dataSize];
    data[0] = (byte)(index & 0xFF);
    data[1] = (byte)((index >> 8) & 0xFF);

    return new VideoFrame(isKeyFrame) {
        Data = data,
        Offset = TimeSpan.FromTicks(frameDuration.Ticks * index),
        Duration = frameDuration,
        Width = isKeyFrame ? FrameWidth : 0,
        Height = isKeyFrame ? FrameHeight : 0,
        Description = isKeyFrame ? [0x00, 0x00, 0x00, 0x01, 0x67] : null,
        Codec = isKeyFrame ? Codec : null,
    };
}

static byte[] SerializeVideoFrame(VideoFrame frame)
{
    var buffer = new ArrayBufferWriter<byte>();
    var writer = new MessagePackWriter(buffer);

    var fieldCount = 3;
    if (frame.IsKeyFrame) fieldCount += 3;
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

static VideoFrame? DeserializeVideoFrame(byte[] bytes)
{
    try {
        var reader = new MessagePackReader(bytes);
        var mapLen = reader.ReadMapHeader();

        long offset = 0, duration = 0;
        var isKeyFrame = false;
        int width = 0, height = 0;
        byte[]? data = null, description = null;
        string? codec = null;

        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case "offset": offset = reader.ReadInt64(); break;
                case "duration": duration = reader.ReadInt64(); break;
                case "isKeyFrame": isKeyFrame = reader.ReadBoolean(); break;
                case "width": width = reader.ReadInt32(); break;
                case "height": height = reader.ReadInt32(); break;
                case "data": data = reader.ReadBytes()?.ToArray(); break;
                case "description": description = reader.TryReadNil() ? null : reader.ReadBytes()?.ToArray(); break;
                case "codec": codec = reader.TryReadNil() ? null : reader.ReadString(); break;
                default: reader.Skip(); break;
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
    catch { return null; }
}

HubConnection CreateHubConnection(string url)
    => new HubConnectionBuilder()
        .WithUrl(url, o => {
            o.SkipNegotiation = true;
            o.Transports = HttpTransportType.WebSockets;
        })
        .AddMessagePackProtocol()
        .Build();

static double Percentile(List<double> values, double percentile)
{
    if (values.Count == 0) return 0;
    var sorted = values.OrderBy(v => v).ToList();
    var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
    return sorted[Math.Max(0, index)];
}

string? GetArg(string shortName, string longName)
{
    var prefix = $"-{shortName}:";
    var value = args.Where(x => x.StartsWith(prefix)).Select(x => x[prefix.Length..]).LastOrDefault();
    if (!value.IsNullOrEmpty()) return value;
    prefix = $"-{longName}:";
    value = args.Where(x => x.StartsWith(prefix)).Select(x => x[prefix.Length..]).LastOrDefault();
    return value.IsNullOrEmpty() ? null : value;
}

IServiceProvider CreateServiceProvider(string serverUrl)
{
    var cfg = new ConfigurationManager();
    var env = Microsoft.Extensions.Hosting.Environments.Development;
    cfg.Sources.Add(new MemoryConfigurationSource {
        InitialData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
            { "DOTNET_ENVIRONMENT", env },
        },
    });

    var svc = new ServiceCollection();
    svc.AddSingleton<IConfiguration>(cfg);
    svc.AddLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddConsole();
    });
    svc.AddTracers(Tracer.Default, useScopedTracers: true);
    svc.AddSingleton(_ => new HostInfo {
        HostKind = HostKind.MauiApp,
        AppKind = AppKind.Windows,
        Configuration = cfg,
        Environment = env,
        BaseUrl = serverUrl,
    });

    var moduleServices = svc.BuildServiceProvider();
    var moduleHostBuilder = new ModuleHostBuilder();
    var moduleHost = moduleHostBuilder.AddModules(
        new CoreModule(moduleServices),
        new ApiModule(moduleServices),
        new ApiContractsModule(moduleServices)
    );
    moduleHost.Build(svc);
    return svc.BuildServiceProvider();
}
