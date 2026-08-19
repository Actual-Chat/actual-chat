using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Video;
using ActualLab.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using static System.Console;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1849 // Method/Property synchronously blocks

RuntimeInfo.IsServer = false;
ApiContractsModuleInitializer.Load();
CoreModuleInitializer.Initialize();

// Ensure enough ThreadPool threads for 300+ concurrent async operations
ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount * 10);

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
// var baseUrl = GetArg("u", "url") ?? "https://local.voxt.ai";
var baseUrl = GetArg("u", "url") ?? "http://localhost:7080";
var durationSec = int.TryParse(GetArg("d", "duration"), out var dd) ? dd : 30;
var testDuration = TimeSpan.FromSeconds(durationSec);
var useRpcBackend = args.Any(x => x is "-rpc-backend" or "--rpc-backend");
// Stagger between producer/consumer spawns (ms). Avoids thundering herd.
var staggerMs = int.TryParse(GetArg("stagger", "stagger"), out var stg) ? stg : 50;

// Worker-mode flag: when set, this process runs only one chat's workload and writes JSON results.
// Absent => orchestrator mode: spawn one worker per chat, aggregate results.
var workerChatIdx = int.TryParse(GetArg("chat-idx", "chat-idx"), out var wci) ? wci : -1;
var keepResults = args.Any(x => x is "--keep-results" or "-keep-results");

// Hardcoded chat IDs (public test chats).
var allChatIdStrings = new[] {
    "zqMxFSJWkS", "weFGfFJNgy", "uKWaKUGZmv", "xPUTIYhnMJ", "Q5wNxJIeVD",
    "3USxtLtliz", "pzOCDCChRR", "D0S4FShrsu", "mMOf4Lj0gw", "NcnBmEfc5e",
};
var originalChatCount = chatCount;

// --- Setup ---
var cts = new CancellationTokenSource();
CancelKeyPress += (_, args) => { args.Cancel = true; cts.Cancel(); };

var mode = useRpcBackend ? "RPC Backend (direct)" : "RPC (API)";

if (workerChatIdx < 0) {
    // --- Orchestrator mode ---
    await RunOrchestrator().ConfigureAwait(false);
    return;
}

// --- Worker mode: scope to a single chat ---
if (workerChatIdx >= originalChatCount || workerChatIdx >= allChatIdStrings.Length)
    throw new InvalidOperationException($"chat-idx={workerChatIdx} out of range (chatCount={originalChatCount}).");
chatCount = 1;

// Each consumer pulls all streams except its own → (consumersPerChat) × (streamsPerChat - 1) pulls per chat
// But consumers may not map 1:1 to producers, so each consumer pulls all streams in its chat
var pullsPerChat = consumersPerChat * (streamsPerChat - 1);
var totalPulls = chatCount * pullsPerChat;
var totalStreams = chatCount * streamsPerChat;

WriteLine($"Video Load Test worker [{mode}] chat-idx={workerChatIdx}: {streamsPerChat} streams × {consumersPerChat} consumers");
WriteLine($"  {totalStreams} total streams, {totalPulls} total pulls for this chat");
WriteLine($"  Base URL: {baseUrl}, Duration: {durationSec}s, Stagger: {staggerMs}ms");

var services = CreateServiceProvider(baseUrl);
var commander = services.Commander();
var session = Session.New();

// Authenticate orchestrator (used for chat discovery and consumers) — chat-scoped to avoid cross-worker contention.
var orchEmail = $"test-videoload-c{workerChatIdx}-o@actual.chat";
WriteLine($"Signing in orchestrator ({orchEmail})...");
var signedIn = await commander
    .Call(new EmailAuth_ValidateTotp(session, Email.New(orchEmail), 111111), cts.Token)
    .ConfigureAwait(false);
if (!signedIn)
    throw new InvalidOperationException("Sign-in failed. Is the server running with test agent bypass?");

// N distinct producer users → N distinct Authors per chat → no stream eviction.
// LiveVideoBackend.Register evicts prior (AuthorId, VideoSourceKind) duplicates in a chat.
// One user per prodIdx (0..streamsPerChat-1), chat-scoped to this worker.
WriteLine($"Signing in {streamsPerChat} producer users (test-videoload-c{workerChatIdx}-p0..p{streamsPerChat - 1})...");
var producerSessions = new Session[streamsPerChat];
var authTasks = new Task[streamsPerChat];
for (var pi = 0; pi < streamsPerChat; pi++) {
    var p = pi;
    var s = Session.New();
    producerSessions[p] = s;
    authTasks[p] = commander.Call(
        new EmailAuth_ValidateTotp(s, Email.New($"test-videoload-c{workerChatIdx}-p{p}@actual.chat"), 111111), cts.Token);
}
await Task.WhenAll(authTasks).ConfigureAwait(false);
foreach (var t in authTasks) {
    if (t is Task<bool> { Result: false })
        throw new InvalidOperationException("Producer sign-in failed. Test agent bypass required.");
}
WriteLine("All producer users signed in.");

// Resolve chat ID for this worker.
var chatIds = new ChatId[] { ChatId.Parse(allChatIdStrings[workerChatIdx]) };
WriteLine($"Using chat #{workerChatIdx}: {chatIds[0].Value}");

// Pre-join every producer session to every chat (otherwise GetRules→Require(Write) fails).
WriteLine($"Joining {streamsPerChat} producer users to {chatCount} chats ({streamsPerChat * chatCount} joins)...");
var joinTasks = new List<Task>();
for (var pi = 0; pi < streamsPerChat; pi++) {
    for (var ci = 0; ci < chatCount; ci++) {
        var s = producerSessions[pi];
        var cid = chatIds[ci];
        joinTasks.Add(commander.Call(new Authors_Join(s, cid), cts.Token));
    }
}
await Task.WhenAll(joinTasks).ConfigureAwait(false);
WriteLine("Producer users joined all chats.");

// --- Metrics ---
// Key: (chatIndex, producerIndex, offsetTicks) → sendTimestamp
var sentTimestamps = new ConcurrentDictionary<(int Chat, int Producer, long Offset), long>();
// Producer's sourceStartOffset — used to correlate discovered StreamId back to prodIdx.
var producerSourceStartOffsets = new ConcurrentDictionary<(int Chat, int Producer), double>();
// Filled after discovery: StreamId.Value → (chatIdx, prodIdx).
var streamToProd = new Dictionary<Symbol, (int Chat, int Producer)>();
// Key: (chatIndex, consumerIndex, streamIndex) → metrics
var latencies = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), ConcurrentBag<double>>();
var framesReceived = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), int>();
var bytesReceived = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), long>();
// First-frame receive timestamp (Stopwatch ticks) per (chat, consumer, stream).
var firstFrameTimestamps = new ConcurrentDictionary<(int Chat, int Consumer, int Stream), long>();
// Reference start: set when consumer spawn loop begins, so we can report first-frame delay.
long consumerStartTicks = 0;

// --- RPC services for direct mode ---
// Each producer + each consumer gets own IServiceProvider → own RpcHub → own WebSocket.
// This matches real topology (one user = one browser tab = one WS) and removes the
// single-peer send-loop bottleneck that starves fan-out under load.
var producerHubs = new IServiceProvider[chatCount, streamsPerChat];
var consumerHubs = new IServiceProvider[chatCount, consumersPerChat];
WriteLine($"Creating {totalStreams} producer hubs + {chatCount * consumersPerChat} consumer hubs...");
var hubSw = Stopwatch.StartNew();
var hubTasks = new List<Task>();
for (var ci = 0; ci < chatCount; ci++) {
    for (var pi = 0; pi < streamsPerChat; pi++) {
        var c = ci;
        var p = pi;
        hubTasks.Add(Task.Run(() => producerHubs[c, p] = CreateServiceProvider(baseUrl)));
    }
    for (var ni = 0; ni < consumersPerChat; ni++) {
        var c = ci;
        var n = ni;
        hubTasks.Add(Task.Run(() => consumerHubs[c, n] = CreateServiceProvider(baseUrl)));
    }
}
await Task.WhenAll(hubTasks).ConfigureAwait(false);
WriteLine($"Hubs ready in {hubSw.ElapsedMilliseconds}ms.");

// --- Start all producers across all chats (staggered) ---
WriteLine($"Starting {totalStreams} producers (stagger={staggerMs}ms)...");
var producerTasks = new List<Task>();
for (var ci = 0; ci < chatCount; ci++) {
    for (var pi = 0; pi < streamsPerChat; pi++) {
        var chatIdx = ci;
        var prodIdx = pi;
        producerTasks.Add(Task.Run(() => RunProducerRpc(chatIdx, prodIdx, cts.Token), cts.Token));
        if (staggerMs > 0) {
            try { await Task.Delay(staggerMs, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
    if (cts.IsCancellationRequested) break;
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
            var computed = await Computed
                .Capture(() => liveVideoStreams.List(session, cid, linkedCts.Token), linkedCts.Token)
                .ConfigureAwait(false);
            if (computed.Value.Count >= streamsPerChat) {
                chatStreams[chatIdx] = computed.Value;
                return;
            }
            await computed.WhenInvalidated(linkedCts.Token).ConfigureAwait(false);
        }
    }, linkedCts.Token);
}
await Task.WhenAll(discoveryTasks).ConfigureAwait(false);
WriteLine($"All {totalStreams} streams discovered across {chatCount} chats.");

// --- Map discovered StreamId → (chatIdx, prodIdx) for latency correlation ---
// Producer's clientStartAt becomes VideoStreamInfo.StartedAt on the server
// (server: beginsAt = default(Moment) + FromSeconds(ClientStartAt)). We match
// against the closest producer offset within the same chat.
for (var ci = 0; ci < chatCount; ci++) {
    var streams = chatStreams[ci];
    for (var si = 0; si < streams.Count; si++) {
        var stream = streams[si];
        var startedAtSec = stream.StartedAt.EpochOffset.TotalSeconds;
        var bestProd = -1;
        var bestDiff = double.MaxValue;
        for (var pi = 0; pi < streamsPerChat; pi++) {
            if (!producerSourceStartOffsets.TryGetValue((ci, pi), out var prodOffset))
                continue;
            var diff = Math.Abs(prodOffset - startedAtSec);
            if (diff < bestDiff) { bestDiff = diff; bestProd = pi; }
        }
        if (bestProd >= 0)
            streamToProd[stream.StreamId.Value] = (ci, bestProd);
    }
}
WriteLine($"Mapped {streamToProd.Count}/{totalStreams} streams to producers for latency correlation.");

// --- Start consumers ---
var consumerTasks = new List<Task>();
WriteLine($"Starting {totalPulls} consumer pulls (stagger={staggerMs}ms per consumer)...");
consumerStartTicks = Stopwatch.GetTimestamp();
for (var ci = 0; ci < chatCount; ci++) {
    var streams = chatStreams[ci];
    for (var consIdx = 0; consIdx < consumersPerChat; consIdx++) {
        // One consumer = one user = one hub. All their pulls fan-out on same WS.
        for (var si = 0; si < streams.Count; si++) {
            if (si == consIdx) continue; // Skip own stream

            var chatIdx = ci;
            var consumerIdx = consIdx;
            var streamIdx = si;
            var streamId = streams[si].StreamId;
            latencies[(chatIdx, consumerIdx, streamIdx)] = new ConcurrentBag<double>();
            framesReceived[(chatIdx, consumerIdx, streamIdx)] = 0;
            bytesReceived[(chatIdx, consumerIdx, streamIdx)] = 0;
            consumerTasks.Add(Task.Run(
                () => useRpcBackend
                    ? RunConsumerRpcBackend(chatIdx, consumerIdx, streamIdx, streamId, cts.Token)
                    : RunConsumerRpc(chatIdx, consumerIdx, streamIdx, streamId, cts.Token),
                cts.Token));
        }
        if (staggerMs > 0) {
            try { await Task.Delay(staggerMs, cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
    if (cts.IsCancellationRequested) break;
}

// --- Wait for test duration ---
WriteLine($"Running for {durationSec}s... (Ctrl+C to stop early)");
try { await Task.Delay(testDuration, cts.Token).ConfigureAwait(false); }
catch (OperationCanceledException) { }

WriteLine("Stopping...");
await cts.CancelAsync().ConfigureAwait(false);

await Task
    .WhenAll(producerTasks.Concat(consumerTasks).Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)))
    .ConfigureAwait(false);

// Graceful RpcHub shutdown — otherwise the server logs WebSocket.ReceiveAsync warnings.
async Task DisposeHubAsync(IServiceProvider? sp)
{
    if (sp == null) return;
    try {
        var rpcHub = sp.GetService<RpcHub>();
        if (rpcHub != null)
            await rpcHub.DisposeAsync().ConfigureAwait(false);
    }
    catch { /* best effort */ }
}
var shutdownTasks = new List<Task> { DisposeHubAsync(services) };
for (var ci = 0; ci < chatCount; ci++) {
    for (var pi = 0; pi < streamsPerChat; pi++)
        shutdownTasks.Add(DisposeHubAsync(producerHubs[ci, pi]));
    for (var ni = 0; ni < consumersPerChat; ni++)
        shutdownTasks.Add(DisposeHubAsync(consumerHubs[ci, ni]));
}
await Task.WhenAll(shutdownTasks).ConfigureAwait(false);

// --- Report ---
PrintReport();

// --- Emit JSON so the orchestrator can aggregate ---
EmitWorkerResults();

// ============================================
// Helper methods
// ============================================

void EmitWorkerResults()
{
    var resultsDir = Path.Combine("tmp", "load-test");
    Directory.CreateDirectory(resultsDir);
    var resultPath = Path.Combine(resultsDir, $"chat-{workerChatIdx}.json");

    var perPull = new List<object>();
    for (var consIdx = 0; consIdx < consumersPerChat; consIdx++) {
        for (var si = 0; si < streamsPerChat; si++) {
            if (si == consIdx) continue;
            var key = (0, consIdx, si);
            var frames = framesReceived.GetValueOrDefault(key);
            var bytesRcvd = bytesReceived.GetValueOrDefault(key);
            var firstMs = firstFrameTimestamps.TryGetValue(key, out var ts) && consumerStartTicks != 0
                ? Stopwatch.GetElapsedTime(consumerStartTicks, ts).TotalMilliseconds
                : -1.0;
            perPull.Add(new {
                consumer = consIdx,
                stream = si,
                frames,
                bytes = bytesRcvd,
                firstFrameMs = firstMs,
            });
        }
    }

    var samples = new List<double>();
    foreach (var kv in latencies)
        samples.AddRange(kv.Value);

    var payload = new {
        chatIdx = workerChatIdx,
        chatId = chatIds[0].ToString(),
        durationSec,
        streamsPerChat,
        consumersPerChat,
        totalPulls,
        perPull,
        latencyMsSamples = samples,
    };

    File.WriteAllText(resultPath, JsonSerializer.Serialize(payload));
    WriteLine($"CHAT {workerChatIdx} DONE {resultPath}");
}

async Task RunOrchestrator()
{
    var resultsDir = Path.Combine("tmp", "load-test");
    Directory.CreateDirectory(resultsDir);
    // Clear previous worker results (for this run's chatCount)
    for (var i = 0; i < originalChatCount; i++) {
        var p = Path.Combine(resultsDir, $"chat-{i}.json");
        if (File.Exists(p)) File.Delete(p);
    }

    WriteLine($"Video Load Test orchestrator [{mode}]: {originalChatCount} chats × {streamsPerChat} streams × {consumersPerChat} consumers");
    WriteLine($"  {originalChatCount * streamsPerChat} total streams, {originalChatCount * consumersPerChat * (streamsPerChat - 1)} total pulls");
    WriteLine($"  Base URL: {baseUrl}, Duration: {durationSec}s, Stagger: {staggerMs}ms");
    WriteLine($"  Spawning {originalChatCount} worker processes...");

    var argv = Environment.GetCommandLineArgs();
    var entryAssembly = argv.Length > 0 ? argv[0] : "";
    var isDotnetHost = entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    var hostExe = Environment.ProcessPath ?? "dotnet";

    // Pass through all original user args, strip any stale -chat-idx.
    var forwardArgs = args.Where(a => !a.StartsWith("-chat-idx", StringComparison.Ordinal)).ToList();

    var children = new Process[originalChatCount];
    var printLock = new object();
    for (var ci = 0; ci < originalChatCount; ci++) {
        var chatIdx = ci;
        var psi = new ProcessStartInfo {
            FileName = isDotnetHost ? hostExe : entryAssembly,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (isDotnetHost) {
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(entryAssembly);
        }
        foreach (var a in forwardArgs)
            psi.ArgumentList.Add(a);
        psi.ArgumentList.Add($"-chat-idx:{chatIdx}");

        var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start worker.");
        children[chatIdx] = p;

        p.OutputDataReceived += (_, e) => {
            if (e.Data == null) return;
            lock (printLock) Console.WriteLine($"[chat {chatIdx}] {e.Data}");
        };
        p.ErrorDataReceived += (_, e) => {
            if (e.Data == null) return;
            lock (printLock) Console.Error.WriteLine($"[chat {chatIdx}] {e.Data}");
        };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }

    // Wait for all workers. On Ctrl+C, terminate them.
    var waits = children.Select(c => c.WaitForExitAsync(cts.Token)).ToArray();
    try { await Task.WhenAll(waits).ConfigureAwait(false); }
    catch (OperationCanceledException) {
        WriteLine("Orchestrator cancelled — terminating workers...");
        foreach (var c in children) {
            try { if (!c.HasExited) c.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
        }
        foreach (var c in children) {
            try { await c.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* Intended */ }
        }
    }

    for (var ci = 0; ci < originalChatCount; ci++) {
        if (children[ci].HasExited && children[ci].ExitCode != 0)
            Error.WriteLine($"[chat {ci}] worker exited with code {children[ci].ExitCode}");
    }

    // --- Aggregate ---
    PrintAggregateReport();

    if (!keepResults) {
        for (var i = 0; i < originalChatCount; i++) {
            var p = Path.Combine(resultsDir, $"chat-{i}.json");
            if (File.Exists(p)) File.Delete(p);
        }
    }
}

void PrintAggregateReport()
{
    var resultsDir = Path.Combine("tmp", "load-test");

    var aggSamples = new List<double>();
    var aggFrames = 0L;
    var aggBytes = 0L;
    var aggFirstFrameDelays = new List<double>();
    var missingChats = new List<int>();
    var totalPulls = 0;      // computed from actual perPull entries
    var perChatRows = new List<(int ci, long frames, long bytes, List<double> samples)>();

    // Pass 1: read all files, compute actual totals.
    for (var ci = 0; ci < originalChatCount; ci++) {
        var path = Path.Combine(resultsDir, $"chat-{ci}.json");
        if (!File.Exists(path)) {
            missingChats.Add(ci);
            continue;
        }
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;

        var chatFrames = 0L;
        var chatBytes = 0L;
        foreach (var pull in root.GetProperty("perPull").EnumerateArray()) {
            totalPulls++;
            chatFrames += pull.GetProperty("frames").GetInt64();
            chatBytes += pull.GetProperty("bytes").GetInt64();
            if (pull.TryGetProperty("firstFrameMs", out var ff) && ff.GetDouble() >= 0)
                aggFirstFrameDelays.Add(ff.GetDouble());
        }
        var chatSamples = new List<double>();
        foreach (var s in root.GetProperty("latencyMsSamples").EnumerateArray())
            chatSamples.Add(s.GetDouble());

        perChatRows.Add((ci, chatFrames, chatBytes, chatSamples));
        aggFrames += chatFrames;
        aggBytes += chatBytes;
        aggSamples.AddRange(chatSamples);
    }

    // Pass 2: print header + per-chat rows + aggregate.
    WriteLine();
    WriteLine("=== VIDEO LOAD TEST RESULTS ===");
    WriteLine($"Duration: {durationSec}s, Chats: {originalChatCount}, Streams/chat: {streamsPerChat}, Consumers/chat: {consumersPerChat}");
    WriteLine($"Total streams: {originalChatCount * streamsPerChat}, Total pulls: {totalPulls}");
    WriteLine();
    WriteLine("--- Per-Chat Summary ---");
    WriteLine($"{"Chat",-6} {"Frames",-10} {"MB/s",-8} {"p50ms",-8} {"p95ms",-8} {"p99ms",-8}");
    foreach (var (ci, chatFrames, chatBytes, chatSamples) in perChatRows) {
        var mbps = chatBytes / (1024.0 * 1024.0) / durationSec;
        WriteLine($"{ci,-6} {chatFrames,-10} {mbps,-8:F2} " +
                  $"{Percentile(chatSamples, 0.50),-8:F1} " +
                  $"{Percentile(chatSamples, 0.95),-8:F1} " +
                  $"{Percentile(chatSamples, 0.99),-8:F1}");
    }
    foreach (var ci in missingChats)
        WriteLine($"{ci,-6} (missing result file)");

    var aggMbps = aggBytes / (1024.0 * 1024.0) / durationSec;
    WriteLine();
    WriteLine("--- Aggregate ---");
    WriteLine($"Total frames received: {aggFrames}");
    WriteLine($"Total bytes: {aggBytes:N0} ({aggMbps:F2} MB/s)");
    if (aggSamples.Count > 0) {
        WriteLine($"Latency p50={Percentile(aggSamples, 0.50):F1}ms, " +
                  $"p95={Percentile(aggSamples, 0.95):F1}ms, " +
                  $"p99={Percentile(aggSamples, 0.99):F1}ms " +
                  $"(samples={aggSamples.Count})");
    }
    else
        WriteLine("Latency: no samples collected across workers.");

    if (aggFirstFrameDelays.Count > 0) {
        var noFirst = totalPulls - aggFirstFrameDelays.Count;
        WriteLine($"First-frame delay: p50={Percentile(aggFirstFrameDelays, 0.50):F0}ms, " +
                  $"p95={Percentile(aggFirstFrameDelays, 0.95):F0}ms, " +
                  $"p99={Percentile(aggFirstFrameDelays, 0.99):F0}ms " +
                  $"(got first frame: {aggFirstFrameDelays.Count}/{totalPulls}, silent: {noFirst})");
    }

    var expectedTotal = (int)(durationSec * 30) * totalPulls;
    WriteLine($"Expected ~{expectedTotal} total frames, got {aggFrames} ({100.0 * aggFrames / Math.Max(1, expectedTotal):F1}%)");
    if (missingChats.Count > 0)
        WriteLine($"WARNING: missing results for chats: {string.Join(",", missingChats)}");
}

// --- RPC-based producer: pushes VideoFrame objects directly via ILiveVideoStreams ---
async Task RunProducerRpc(int chatIdx, int prodIdx, CancellationToken ct)
{
    try {
        var ownLiveVideoStreams = producerHubs[chatIdx, prodIdx].GetRequiredService<ILiveVideoStreams>();
        var prodSession = producerSessions[prodIdx];
        var sourceStartOffsetSeconds = CpuClock.Instance.Now.EpochOffset.TotalSeconds;
        producerSourceStartOffsets[(chatIdx, prodIdx)] = sourceStartOffsetSeconds;
        var format = new VideoFormat { Codec = Codec, Size = (FrameWidth, FrameHeight) };
        var bundleStream = RpcStream.New(PushFrameBundlesRpc(chatIdx, prodIdx, ct));
        await ownLiveVideoStreams.PushStream(
            prodSession, chatIds[chatIdx].Value, sourceStartOffsetSeconds,
            format, VideoSourceKind.Camera, bundleStream, ct
            ).ConfigureAwait(false);
        try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
    catch (Exception e) when (e is not OperationCanceledException) {
        Error.WriteLine($"ProducerRpc[chat={chatIdx},prod={prodIdx}] error: {e.GetType().Name}: {e.Message}");
    }
}

async IAsyncEnumerable<VideoFrameBundle> PushFrameBundlesRpc(
    int chatIdx, int prodIdx,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
{
    var sw = Stopwatch.StartNew();
    for (var i = 0; !ct.IsCancellationRequested; i++) {
        var frame = GenerateFrame(i);

        var targetElapsed = TimeSpan.FromTicks(frameDuration.Ticks * i);
        var remaining = targetElapsed - sw.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct).ConfigureAwait(false);

        sentTimestamps[(chatIdx, prodIdx, frame.Offset.Ticks)] = Stopwatch.GetTimestamp();
        yield return new VideoFrameBundle([frame]);
    }
}

// --- RPC-based consumer: pulls VideoFrame objects directly via ILiveVideoStreams ---
async Task RunConsumerRpc(int chatIdx, int consumerIdx, int streamIdx, StreamId streamId, CancellationToken ct)
{
    try {
        var ownLiveVideoStreams = consumerHubs[chatIdx, consumerIdx].GetRequiredService<ILiveVideoStreams>();
        var rpcStream = await ownLiveVideoStreams.GetStream(session, streamId, ct).ConfigureAwait(false);
        if (rpcStream == null) {
            Error.WriteLine($"ConsumerRpc[chat={chatIdx},cons={consumerIdx},stream={streamIdx}] stream not found");
            return;
        }

        await foreach (var frame in rpcStream.WithCancellation(ct).ConfigureAwait(false)) {
            if (ct.IsCancellationRequested) break;

            var receiveTs = Stopwatch.GetTimestamp();
            var key = (chatIdx, consumerIdx, streamIdx);
            var frameSize = frame.Data.Length + 20;
            framesReceived.AddOrUpdate(key, 1, (_, v) => v + 1);
            bytesReceived.AddOrUpdate(key, frameSize, (_, v) => v + frameSize);
            firstFrameTimestamps.TryAdd(key, receiveTs);

            if (streamToProd.TryGetValue(streamId.Value, out var prodKey) &&
                sentTimestamps.TryGetValue((prodKey.Chat, prodKey.Producer, frame.Offset.Ticks), out var sentTs)) {
                var latencyMs = Stopwatch.GetElapsedTime(sentTs, receiveTs).TotalMilliseconds;
                latencies[key].Add(latencyMs);
            }
        }
    }
    catch (Exception e) when (e is OperationCanceledException or IOException) {
        // Expected on shutdown
    }
    catch (Exception e) {
        Error.WriteLine($"ConsumerRpc[chat={chatIdx},cons={consumerIdx},stream={streamIdx}] error: {e.GetType().Name}: {e.Message}");
    }
}

// --- RPC Backend consumer: calls IVideoStreamingBackend.GetVideoRaw() directly, bypassing API layer ---
async Task RunConsumerRpcBackend(int chatIdx, int consumerIdx, int streamIdx, StreamId streamId, CancellationToken ct)
{
    try {
        var videoBackend = services.GetRequiredService<IVideoStreamingBackend>();
        var rpcStream = await videoBackend.GetVideoRaw(streamId, ct).ConfigureAwait(false);
        if (rpcStream == null) {
            Error.WriteLine($"ConsumerRpcBackend[chat={chatIdx},cons={consumerIdx},stream={streamIdx}] stream not found");
            return;
        }

        await foreach (var frame in rpcStream.WithCancellation(ct).ConfigureAwait(false)) {
            if (ct.IsCancellationRequested) break;

            var receiveTs = Stopwatch.GetTimestamp();
            var key = (chatIdx, consumerIdx, streamIdx);
            var frameSize = frame.Data.Length + 20;
            framesReceived.AddOrUpdate(key, 1, (_, v) => v + 1);
            bytesReceived.AddOrUpdate(key, frameSize, (_, v) => v + frameSize);
            firstFrameTimestamps.TryAdd(key, receiveTs);

            if (streamToProd.TryGetValue(streamId.Value, out var prodKey) &&
                sentTimestamps.TryGetValue((prodKey.Chat, prodKey.Producer, frame.Offset.Ticks), out var sentTs)) {
                var latencyMs = Stopwatch.GetElapsedTime(sentTs, receiveTs).TotalMilliseconds;
                latencies[key].Add(latencyMs);
            }
        }
    }
    catch (Exception e) when (e is OperationCanceledException or IOException) {
        // Expected on shutdown
    }
    catch (Exception e) {
        Error.WriteLine($"ConsumerRpcBackend[chat={chatIdx},cons={consumerIdx},stream={streamIdx}] error: {e.GetType().Name}: {e.Message}");
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
                  $"p99={Percentile(aggLatencies, 0.99):F1}ms " +
                  $"(samples={aggLatencies.Count})");
    }
    else
        WriteLine("Latency: no samples (producer↔stream mapping missing)");

    // First-frame delay: how long each consumer waited from spawn time until first frame arrived
    if (consumerStartTicks != 0 && !firstFrameTimestamps.IsEmpty) {
        var firstFrameDelaysMs = firstFrameTimestamps.Values
            .Select(ts => Stopwatch.GetElapsedTime(consumerStartTicks, ts).TotalMilliseconds)
            .ToList();
        var noFirstFrame = totalPulls - firstFrameDelaysMs.Count;
        WriteLine($"First-frame delay (consumer spawn → first frame): " +
                  $"p50={Percentile(firstFrameDelaysMs, 0.50):F0}ms, " +
                  $"p95={Percentile(firstFrameDelaysMs, 0.95):F0}ms, " +
                  $"p99={Percentile(firstFrameDelaysMs, 0.99):F0}ms " +
                  $"(got first frame: {firstFrameDelaysMs.Count}/{totalPulls}, silent: {noFirstFrame})");
    }

    var expectedFramesPerPull = durationSec * 30;
    var expectedTotal = expectedFramesPerPull * totalPulls;
    WriteLine($"Expected ~{expectedTotal} total frames, got {aggFrames} ({100.0 * aggFrames / Math.Max(1, expectedTotal):F1}%)");
}

// ============================================
// Frame generation
// ============================================

VideoFrame GenerateFrame(int index)
{
    var isKeyFrame = index % GopSize == 0;
    var dataSize = isKeyFrame ? KeyFrameDataSize : DeltaFrameDataSize;
    var data = CreateRandomBytes(dataSize);
    data[0] = (byte)(index & 0xFF);
    data[1] = (byte)((index >> 8) & 0xFF);

    var keyFrameIndex = index - (index % GopSize);
    return new VideoFrame {
        Data = data,
        Offset = TimeSpan.FromTicks(frameDuration.Ticks * index),
        Duration = frameDuration,
        Index = index,
        KeyFrameIndex = keyFrameIndex,
        Width = isKeyFrame ? FrameWidth : 0,
        Height = isKeyFrame ? FrameHeight : 0,
        Description = isKeyFrame ? new byte[] { 0x00, 0x00, 0x00, 0x01, 0x67 } : default,
        Codec = isKeyFrame ? Codec : null,
    };
}

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

    // Register backend RPC client for --rpc-backend mode (direct calls bypassing API layer)
    if (useRpcBackend) {
        var fusion = svc.AddFusion();
        fusion.AddClient<IVideoStreamingBackend>();
    }

    return svc.BuildServiceProvider();
}

static byte[] CreateRandomBytes(int size)
{
    var bytes = new byte[size];
    Random.Shared.NextBytes(bytes);
    return bytes;
}
