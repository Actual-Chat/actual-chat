using System.Net.WebSockets;
using System.Text;
using ActualChat.Audio;

namespace ActualChat.Transcription.IntegrationTests;

// Diagnostic: replays one recording over Soniox's realtime API under a matrix of settings and reports
// how close each variant lands to a known-good offline transcript, how far visible text lags speech,
// and how often the model actually revises text it already showed.

[Collection(nameof(TranscriptionCollection))]
public sealed class SonioxEndpointMatrixTest(ITestOutputHelper @out, ILogger<SonioxEndpointMatrixTest> log)
    : TranscriberTestBase(@out, log)
{
    private const string Url = "wss://stt-rt.soniox.com/transcribe-websocket";
    private const string Clip = "long-ru-en-1.webm";
    private static readonly int[] PageDurations = [20, 60, 100, 200];
    private static readonly int[] MaxEndpointDelays = [500, 2000, 3000];
    private static readonly double[] EndpointSensitivities = [-0.5, 0.0, 0.5];

    // Produced by the OpenAI offline pass over Clip, i.e. what a second pass can do with the whole
    // recording in hand. Realtime output is scored against it - it's a reference, not ground truth.
    private const string Reference = """
        Окей, и ещё один тест на всю эту херню с транскрипцией. Посмотрим, как оно работает, и посмотрим,
        как ты финализируешь всё это добро. Что-то вот я не вижу, что ты его финализируешь, и мне кажется,
        что ты всё это ретранскрибируешь в самом конце, и это будет, конечно, очень странно. Okay, my guess
        is that we need to change these settings that control retranscription. Basically, what happens is
        that with the current settings, it always waits till the end of the message. It basically doesn't
        get a chance to transcribe it even a single time in the middle of a sentence, because our voice
        activity detection at some points aborts. I mean, this basically stops the message anyway, and
        that's where I guess it gets a chance to retranscribe it. So I think, well, okay, давай попробуем
        ещё эту херню на русском теперь сказать и посмотрим, что там получится: Коты транскрибировали,
        транскрибировали, да не транскрибировались. Коты транскрибировали, транскрибировали, да не
        транскрибировались.
        """;

    [Fact(Skip = "Diagnostic, for manual runs only")]
    public async Task SweepEndpointParams()
    {
        var apiKey = GetApiKey();
        if (apiKey.IsNullOrEmpty())
            return;

        // arrange
        var audio = await GetAudio(Clip);
        var chunks = await ToOggChunks(audio, Constants.Transcription.StreamPageDuration);
        var configs = (
            from delay in MaxEndpointDelays
            from sensitivity in EndpointSensitivities
            select new RunConfig((int)Constants.Transcription.StreamPageDuration.TotalMilliseconds, delay, sensitivity)
            ).ToList();

        // act
        var results = await configs
            .Select(x => Run(apiKey, chunks, x))
            .Collect(CancellationToken.None);

        // assert
        Report(results);
        foreach (var r in results.OrderByDescending(x => x.Similarity).Take(3))
            WriteLine($"\n--- {r.Config} (similarity {r.Similarity:P1})\n{r.Text}");

        var best = results.MaxBy(x => x.Similarity)!;
        WriteLine($"\nBest: {best.Config} at {best.Similarity:P1}");
        foreach (var revision in results.SelectMany(x => x.Revisions).Take(10))
            WriteLine(revision);
    }

    [Fact(Skip = "Diagnostic, for manual runs only")]
    public async Task SweepPageDuration()
    {
        var apiKey = GetApiKey();
        if (apiKey.IsNullOrEmpty())
            return;

        // arrange
        var audio = await GetAudio(Clip);
        var runTasks = new List<Task<RunResult>>();
        foreach (var pageDurationMs in PageDurations) {
            var chunks = await ToOggChunks(audio, TimeSpan.FromMilliseconds(pageDurationMs));
            runTasks.Add(Run(apiKey, chunks, new RunConfig(pageDurationMs, 2000, 0.0)));
        }

        // act
        var results = await runTasks.Collect(CancellationToken.None);

        // assert
        Report(results);
    }

    // Private methods

    private string GetApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("CoreSettings__SonioxKey") ?? "";
        if (apiKey.IsNullOrEmpty())
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");

        return apiKey;
    }

    private void Report(IReadOnlyList<RunResult> results)
    {
        WriteLine("");
        WriteLine("page | delay | sens | bytes | ends | avgLag | maxLag | grows | revises | similarity");
        foreach (var r in results)
            WriteLine($"{r.Config.PageDurationMs,4} | {r.Config.MaxEndpointDelayMs,5} | "
                + $"{r.Config.EndpointSensitivity,4:0.0} | {r.Bytes,5} | {r.Endpoints,4} | "
                + $"{r.AvgLagMs,6} | {r.MaxLagMs,6} | {r.Grows,5} | {r.Revisions.Count,7} | "
                + $"{r.Similarity,10:P1}");
    }

    private async Task<RunResult> Run(string apiKey, IReadOnlyList<Chunk> chunks, RunConfig config)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(Url), CancellationToken.None).ConfigureAwait(false);
        var request = new Dictionary<string, object?> {
            ["api_key"] = apiKey,
            ["model"] = "stt-rt-v5",
            ["audio_format"] = "auto",
            ["language_hints"] = new[] { "ru", "en" },
            ["enable_endpoint_detection"] = true,
            ["max_endpoint_delay_ms"] = config.MaxEndpointDelayMs,
            ["endpoint_sensitivity"] = config.EndpointSensitivity,
        };
        await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)),
                WebSocketMessageType.Text, true, CancellationToken.None)
            .ConfigureAwait(false);

        var startedAt = DateTime.UtcNow;
        var readTask = Read(ws, startedAt, config);
        foreach (var (chunk, audioEndsAt) in chunks) {
            // Scheduled against the audio position each page carries, not against PageDuration: a page
            // overshoots the requested duration by up to one frame, so a fixed per-chunk delay would
            // run wall clock ahead of the audio and quietly deflate every lag below.
            var delay = startedAt + audioEndsAt - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);

            await ws.SendAsync(chunk, WebSocketMessageType.Binary, true, CancellationToken.None)
                .ConfigureAwait(false);
        }

        await ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, true, CancellationToken.None)
            .ConfigureAwait(false);
        var result = await readTask.ConfigureAwait(false);

        return result with { Bytes = chunks.Sum(x => x.Bytes.Length) };
    }

    private static async Task<RunResult> Read(ClientWebSocket ws, DateTime startedAt, RunConfig config)
    {
        var buffer = new ArraySegment<byte>(new byte[16 * 1024]);
        var message = new StringBuilder();
        var finalText = new StringBuilder();
        var endpoints = 0;
        var grows = 0;
        var revisions = new List<string>();
        var lastVisible = "";
        var lags = new List<long>();
        var maxSeenEndMs = -1L;
        while (ws.State == WebSocketState.Open) {
            message.Clear();
            WebSocketReceiveResult result;
            var isClosed = false;
            do {
                result = await ws.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) {
                    isClosed = true;
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer.Array!, 0, result.Count));
            } while (!result.EndOfMessage);

            if (isClosed)
                break;

            var response = JsonSerializer.Deserialize<SonioxResponse>(message.ToString());
            if (response?.ErrorCode is { } errorCode)
                throw StandardError.External($"Soniox error {errorCode}: {response.ErrorMessage}");
            if (response == null)
                continue;

            var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            var tail = new StringBuilder();
            foreach (var token in response.Tokens ?? []) {
                if (token.Text == "<end>") {
                    endpoints++;
                    continue;
                }

                // Leading edge: the first time any text covering this audio position shows up,
                // final or not. That's what a reader perceives as the transcription lag.
                if (token.EndMs > maxSeenEndMs) {
                    maxSeenEndMs = token.EndMs;
                    lags.Add(elapsedMs - token.EndMs);
                }

                if (token.IsFinal)
                    finalText.Append(token.Text);
                else
                    tail.Append(token.Text);
            }

            // Measured on final + tail rather than the tail alone: finalization moves words out of the
            // tail without changing anything on screen, and that shrinkage reads as a false revision.
            var visible = finalText + tail.ToString();
            if (visible.StartsWith(lastVisible))
                grows += visible.Length == lastVisible.Length ? 0 : 1;
            else
                revisions.Add($"[{config}] {Diverge(lastVisible, visible)}");

            lastVisible = visible;
            if (response.Finished)
                break;
        }

        return new RunResult {
            Config = config,
            Endpoints = endpoints,
            AvgLagMs = lags.Count == 0 ? -1 : (long)lags.Average(),
            MaxLagMs = lags.Count == 0 ? -1 : lags.Max(),
            Grows = grows,
            Revisions = revisions,
            Text = lastVisible,
            Similarity = Similarity(lastVisible),
        };
    }

    private async Task<IReadOnlyList<Chunk>> ToOggChunks(AudioSource audioSource, TimeSpan pageDuration)
    {
        var converter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = pageDuration,
        });
        var chunks = new List<Chunk>();
        var audioEndsAt = TimeSpan.Zero;
        await foreach (var (bytes, lastFrame) in converter.ToByteFrameStream(audioSource, CancellationToken.None)) {
            if (lastFrame != null)
                audioEndsAt = lastFrame.Offset + lastFrame.Duration;

            chunks.Add(new Chunk(bytes, audioEndsAt));
        }

        return chunks;
    }

    private static double Similarity(string text)
        => LinearMapDtwRemapper
            .RemapWithSimilarity(Reference, text, LinearMap.Zero, LinearMapAlignmentMode.RetranscribeSameAudio)
            .Similarity;

    private static string Diverge(string old, string @new)
    {
        var i = 0;
        while (i < old.Length && i < @new.Length && old[i] == @new[i])
            i++;
        var from = Math.Max(0, i - 20);

        return $"…{old[from..]} -> …{@new[from..]}";
    }

    // Nested types

    private sealed record Chunk(byte[] Bytes, TimeSpan AudioEndsAt);

    private sealed record RunConfig(int PageDurationMs, int MaxEndpointDelayMs, double EndpointSensitivity)
    {
        public override string ToString()
            => $"page={PageDurationMs} delay={MaxEndpointDelayMs} sens={EndpointSensitivity:0.0}";
    }

    private sealed record RunResult
    {
        public RunConfig Config { get; init; } = null!;
        public int Bytes { get; init; }
        public int Endpoints { get; init; }
        public long AvgLagMs { get; init; }
        public long MaxLagMs { get; init; }
        public int Grows { get; init; }
        public IReadOnlyList<string> Revisions { get; init; } = [];
        public string Text { get; init; } = "";
        public double Similarity { get; init; }
    }
}
