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
        client.StreamCount.Should().Be(1);
    }

    [Fact]
    public async Task TtsShouldRollOverPastMaxStreamDuration()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var client = new SonioxTtsClient(services, new SonioxTtsClient.Options {
            MaxStreamDuration = TimeSpan.FromSeconds(1),
        });
        var text = Channel.CreateUnbounded<string>();
        var pcm = Channel.CreateUnbounded<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // act
        var runTask = client.Run("test", "en", "Adrian", text.Reader, pcm.Writer, cts.Token);
        var drainTask = pcm.Reader.ReadAllAsync().Select(c => (long)c.Length).SumAsync().AsTask();
        for (var i = 0; i < 3; i++) {
            text.Writer.TryWrite($"Sentence number {i + 1} is long enough to take a couple of seconds to say out loud.");
            // Rollover is decided on the audio already generated, so give each sentence time to land
            await Task.Delay(TimeSpan.FromSeconds(4));
        }
        text.Writer.Complete();
        await runTask;
        var totalBytes = await drainTask;

        // assert
        WriteLine($"{client.StreamCount} streams, {totalBytes / (double)BytesPerSecond:F1}s of audio");
        client.StreamCount.Should().BeGreaterThanOrEqualTo(2,
            "every sentence exceeds the 1s cap, so the second one must open a new stream");
        totalBytes.Should().BeGreaterThan(3 * BytesPerSecond);
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
