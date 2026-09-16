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

    [Fact]
    public async Task TtsShouldReturnOpusFramesForStreamedText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.TryWrite("Hello there, this is a test of the dubbing pipeline.");
        text.Writer.TryWrite(" And here is one more sentence.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startedAt = CpuTimestamp.Now;
        var firstFrameAt = TimeSpan.Zero;
        var readTask = Task.Run(async () => {
            var frames = new List<AudioFrame>();
            await foreach (var frame in output.Reader.ReadAllAsync(cts.Token)) {
                if (frames.Count == 0)
                    firstFrameAt = startedAt.Elapsed;
                frames.Add(frame);
            }
            return frames;
        }, cts.Token);

        // act
        await client.Run("test", "en", "Adrian", text.Reader, output.Writer, null, cts.Token);
        var frames = await readTask;

        // assert
        WriteLine($"{frames.Count} frames = {frames.Count / (double)FramesPerSecond:F1}s of audio, "
            + $"{frames.Sum(f => f.Data.Length) / 1024.0:F1} KB, first frame at {firstFrameAt.TotalSeconds:F1}s");
        frames.Count.Should().BeGreaterThan(FramesPerSecond, "two sentences are well over a second of speech");
        firstFrameAt.Should().BeLessThan(TimeSpan.FromSeconds(5), "the first frame arrives well before the end");
        for (var i = 0; i < frames.Count; i++)
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
        frames.Should().OnlyContain(f => f.Duration == Constants.Audio.OpusFrameDuration);
        client.StreamCount.Should().Be(1, "chunks of one utterance share a stream");
    }

    [Fact]
    public async Task TtsShouldKeepOneStreamForSteadyChunks()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var text = Channel.CreateUnbounded<string>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startedAt = CpuTimestamp.Now;
        var firstAudioAt = TimeSpan.Zero;
        var readTask = Task.Run(async () => {
            var frameCount = 0;
            await foreach (var _ in output.Reader.ReadAllAsync(cts.Token)) {
                if (frameCount == 0)
                    firstAudioAt = startedAt.Elapsed;
                frameCount++;
            }
            return frameCount;
        }, cts.Token);

        // act
        var runTask = client.Run("test", "en", "Adrian", text.Reader, output.Writer, null, cts.Token);
        for (var i = 1; i <= 7; i++) {
            text.Writer.TryWrite($"Chunk number {i} of a steady stream keeps the stream alive and speaking, ");
            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }
        text.Writer.Complete();
        await runTask;
        var frameCount = await readTask;

        // assert
        WriteLine($"{client.StreamCount} streams, {frameCount / (double)FramesPerSecond:F1}s of audio, "
            + $"first audio at {firstAudioAt.TotalSeconds:F1}s, done at {startedAt.Elapsed.TotalSeconds:F1}s");
        client.StreamCount.Should().Be(1, "chunks arriving every second never let the stream go idle");
        frameCount.Should().BeGreaterThan(5 * FramesPerSecond, "seven phrases are well over five seconds of speech");
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
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        // act
        var runTask = client.Run("test", "en", "Adrian", text.Reader, output.Writer, null, cts.Token);
        text.Writer.TryWrite("This chunk is spoken first.");
        await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
        text.Writer.TryWrite("This chunk arrives well after the first stream was ended as idle.");
        text.Writer.Complete();
        await runTask;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        WriteLine($"{client.StreamCount} streams, {frames.Count / (double)FramesPerSecond:F1}s of audio");
        frames.Count.Should().BeGreaterThan(3 * FramesPerSecond, "both chunks should be spoken");
        client.StreamCount.Should().Be(2, "the idle flush ends the first stream, so the late chunk opens another");
        for (var i = 0; i < frames.Count; i++)
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i, "offsets run on across streams");
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
        var output = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // act
        await client.Run("test", "en", "Adrian", text.Reader, output.Writer, null, cts.Token);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().BeEmpty("no text was ever sent, so no stream should have opened");
        client.StreamCount.Should().Be(0);
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
        var output = Channel.CreateUnbounded<AudioFrame>();
        var listener = new SpeakStartListener();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        // act
        var runTask = client.Run("test", "en", "Adrian", textChannel.Reader, output.Writer, listener, cts.Token);
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
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        var firstAudioAt = listener.AudioStartedAt - listener.StreamOpenedAt;
        WriteLine($"{name}: first audio {firstAudioAt.TotalSeconds:F2}s, {frames.Count} frames");
        frames.Count.Should().BeGreaterThan(0);
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
