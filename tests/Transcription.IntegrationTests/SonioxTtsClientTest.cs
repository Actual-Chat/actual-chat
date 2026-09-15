using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxTtsClientTest(ITestOutputHelper @out, ILogger<SonioxTtsClientTest> log)
    : TranscriberTestBase(@out, log)
{
    private const int BytesPerSecond = 48_000 * sizeof(short);

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
        text.Writer.TryWrite("Hello there, this is a test of the dubbing pipeline.");
        text.Writer.TryWrite(" And here is one more sentence.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act
        await client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{chunks.Count} chunks, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        totalBytes.Should().BeGreaterThan(BytesPerSecond, "two sentences are well over a second of speech");
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
        var runTask = client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        for (var i = 1; i <= 7; i++) {
            text.Writer.TryWrite($"Chunk number {i} of a steady stream keeps the stream alive and speaking, ");
            await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        }
        text.Writer.Complete();
        await runTask;
        var totalBytes = await readTask;

        // assert
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio, "
            + $"first audio at {firstAudioAt.TotalSeconds:F1}s, done at {startedAt.Elapsed.TotalSeconds:F1}s");
        client.StreamCount.Should().Be(1, "chunks arriving every second never let the stream go idle");
        totalBytes.Should().BeGreaterThan(5 * BytesPerSecond, "seven phrases are well over five seconds of speech");
    }

    [Fact]
    public async Task GenerateShouldReturnPcmForAWholeText()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services);
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        // act
        await client.Generate("en", "Adrian", "One", pcm.Writer, cts.Token);
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{chunks.Count} chunks, {totalBytes / (double)BytesPerSecond:F2}s of audio");
        totalBytes.Should().BeGreaterThan(BytesPerSecond / 10, "one word is at least a tenth of a second of speech");
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
        var runTask = client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        text.Writer.TryWrite("This chunk is spoken first.");
        await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
        text.Writer.TryWrite("This chunk arrives well after the first stream was ended as idle.");
        text.Writer.Complete();
        await runTask;
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        totalBytes.Should().BeGreaterThan(3 * BytesPerSecond, "both chunks should be spoken");
        client.StreamCount.Should().Be(2, "the idle flush ends the first stream, so the late chunk opens another");
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
        await client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        chunks.Should().BeEmpty("no text was ever sent, so no stream should have opened");
        client.StreamCount.Should().Be(0);
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
}
