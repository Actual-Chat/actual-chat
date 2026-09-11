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
        client.StreamCount.Should().Be(2, "each text chunk opens and closes its own stream");
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
        await Task.Delay(TimeSpan.FromSeconds(12), cts.Token);
        text.Writer.TryWrite("This chunk arrives well after a shared connection would have gone idle.");
        text.Writer.Complete();
        await runTask;
        var chunks = await pcm.Reader.ReadAllAsync().ToListAsync();

        // assert
        var totalBytes = chunks.Sum(c => (long)c.Length);
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        totalBytes.Should().BeGreaterThan(BytesPerSecond / 2, "a chunk arriving late should still speak normally");
        client.StreamCount.Should().Be(1, "the single chunk opens exactly one connection and stream");
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
