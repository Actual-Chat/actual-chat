using ActualChat.Audio;
using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxTtsClientTest(ITestOutputHelper @out, ILogger<SonioxTtsClientTest> log)
    : TranscriberTestBase(@out, log)
{
    private const int FramesPerSecond = 1000 / Constants.Audio.OpusFrameDurationMs;
    private const int BytesPerSecond = OpusFramePump.SampleRate * sizeof(short);

    [Fact]
    public async Task TtsShouldReturnPcmForStreamedText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new SpeakStartListener();
        text.Writer.TryWrite("Hello there, this is a test of the dubbing pipeline.");
        text.Writer.TryWrite(" And here is one more sentence.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startedAt = CpuTimestamp.Now;
        var firstFrameAt = TimeSpan.Zero;
        var readTask = Task.Run(async () => {
            var chunks = new List<byte[]>();
            var byteCount = 0L;
            await foreach (var chunk in pcm.Reader.ReadAllAsync(cts.Token)) {
                byteCount += chunk.Length;
                if (firstFrameAt == TimeSpan.Zero && byteCount >= OpusFramePump.FrameByteLength)
                    firstFrameAt = startedAt.Elapsed;
                chunks.Add(chunk);
            }
            return chunks;
        }, cts.Token);

        // act
        await client.Run(
            "test", "en", "Adrian", null,
            text.Reader, pcm.Writer, listener, cts.Token);
        var chunks = await readTask;

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        var firstAudioAfterOpen = listener.AudioStartedAt - listener.StreamOpenedAt;
        WriteLine($"{chunks.Count} chunks = {totalBytes / (double)BytesPerSecond:F1}s of audio, "
            + $"{totalBytes / 1024.0:F1} KB, first frame at {firstFrameAt.TotalSeconds:F2}s "
            + $"({firstAudioAfterOpen.TotalSeconds:F2}s after the first text was sent)");
        totalBytes.Should().BeGreaterThan(BytesPerSecond, "two sentences are well over a second of speech");
        firstFrameAt.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "PCM comes in 256 ms chunks, not the 1 s Ogg pages Opus came in (measured 2.48 s)");
        chunks.Should().OnlyContain(c => c.Length % sizeof(short) == 0, "s16le chunks are sample-aligned");
        client.StreamCount.Should().Be(2, "every chunk is spoken on a stream of its own");
    }

    [Fact]
    public async Task TtsShouldSpeakEachChunkOnItsOwnStream()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startedAt = CpuTimestamp.Now;
        var firstAudioAt = TimeSpan.Zero;
        var readTask = Task.Run(async () => {
            var totalBytes = 0L;
            await foreach (var chunk in pcm.Reader.ReadAllAsync(cts.Token)) {
                if (totalBytes == 0)
                    firstAudioAt = startedAt.Elapsed;
                totalBytes += chunk.Length;
            }
            return totalBytes;
        }, cts.Token);

        // act
        var runTask = client.Run(
            "test", "en", "Adrian", null,
            text.Reader, pcm.Writer, null, cts.Token);
        for (var i = 1; i <= 5; i++) {
            text.Writer.TryWrite($"This is clause number {i}.");
            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }
        text.Writer.Complete();
        await runTask;
        var totalBytes = await readTask;

        // assert
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio, "
            + $"first audio at {firstAudioAt.TotalSeconds:F1}s, done at {startedAt.Elapsed.TotalSeconds:F1}s");
        client.StreamCount.Should().BeInRange(5, 6,
            "one stream per chunk, plus at most the one pre-opened for a chunk that never came");
        totalBytes.Should().BeGreaterThan(5 * BytesPerSecond, "five clauses are well over five seconds of speech");
    }

    [Fact]
    public async Task GenerateShouldReturnOpusFramesForAWholeText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // act
        await client.Generate("en", "Adrian", "One", output.Writer, cts.Token);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        WriteLine($"{frames.Count} frames = {frames.Count / (double)FramesPerSecond:F2}s of audio");
        frames.Count.Should().BeGreaterThan(FramesPerSecond / 10, "one word is at least a tenth of a second of speech");
        for (var i = 0; i < frames.Count; i++)
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
    }

    [Fact]
    public async Task TtsShouldSpeakAChunkThatArrivesLate()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        // act
        var runTask = client.Run(
            "test", "en", "Adrian", null,
            text.Reader, pcm.Writer, null, cts.Token);
        text.Writer.TryWrite("This chunk is spoken first.");
        await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
        text.Writer.TryWrite("This chunk arrives well after the stream pre-opened for it was ended as idle.");
        text.Writer.Complete();
        await runTask;
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        totalBytes.Should().BeGreaterThan(3 * BytesPerSecond, "both chunks should be spoken");
        client.StreamCount.Should().Be(3,
            "each chunk is its own stream, and the one pre-opened between them idles out");
    }

    [Fact]
    public async Task TtsShouldEndCleanlyWithNoText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // act
        await client.Run(
            "test", "en", "Adrian", null,
            text.Reader, pcm.Writer, null, cts.Token);
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        chunks.Should().BeEmpty();
        client.StreamCount.Should().Be(0, "no stream is opened ahead of a text that is already over");
    }

    [Theory(Skip = "Diagnostic, for manual runs only")]
    [InlineData("fragment", "Hello there my dear", 0)]
    [InlineData("sentence", "Hello there my dear friend.", 0)]
    [InlineData("two-chunks", "Hello there my dear|friend, how are you", 2500)]
    [InlineData("ended", "Hello there my dear", -1)]
    [InlineData("long-fragment", "Hello there my dear friend how are you doing today and what is new", 0)]
    [InlineData("comma", "Hello there my dear friend,", 0)]
    public async Task TtsShouldRevealWhenSonioxStartsSpeaking(string name, string text, int gapMs)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services) { IdleFlush = TimeSpan.FromSeconds(6) };
        var textChannel = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        var listener = new SpeakStartListener();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        // act
        var runTask = client.Run(
            "test", "en", "Adrian", null,
            textChannel.Reader, pcm.Writer, listener, cts.Token);
        var chunks = text.Split('|');
        for (var i = 0; i < chunks.Length; i++) {
            textChannel.Writer.TryWrite(chunks[i]);
            if (gapMs == -1)
                break;
            if (i < chunks.Length - 1)
                await Task.Delay(gapMs, cts.Token);
        }
        if (gapMs == -1)
            textChannel.Writer.Complete();
        else {
            await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);
            textChannel.Writer.Complete();
        }
        await runTask;
        var audio = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var firstAudioAt = listener.AudioStartedAt - listener.StreamOpenedAt;
        var totalBytes = audio.Sum(c => (long)c.Length);
        WriteLine($"{name}: first audio {firstAudioAt.TotalSeconds:F2}s, {totalBytes / (double)BytesPerSecond:F1}s");
        audio.Should().NotBeEmpty();
    }

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }

    // Nested types

    private sealed class SpeakStartListener : ISpeechSynthesisListener
    {
        public CpuTimestamp StreamOpenedAt { get; private set; }
        public CpuTimestamp AudioStartedAt { get; private set; }

        public void OnStreamOpened() => StreamOpenedAt = CpuTimestamp.Now;
        public void OnAudioStarted() => AudioStartedAt = CpuTimestamp.Now;
    }
}
