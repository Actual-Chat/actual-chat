using ActualChat.Audio;
using ActualChat.Module;
using ActualChat.Transcription.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public sealed class SonioxSpeechSynthesizerTest(ITestOutputHelper @out, ILogger<SonioxSpeechSynthesizerTest> log)
    : TranscriberTestBase(@out, log)
{
    [Fact]
    public async Task SynthesizerShouldProduceAudibleOpusFrames()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var synthesizer = new SonioxSpeechSynthesizer(services);
        var text = Channel.CreateUnbounded<string>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        text.Writer.TryWrite("Привет, это проверка синтеза речи.");
        text.Writer.Complete();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act
        await synthesizer.Synthesize(
            "test", text.Reader, new SpeechSynthesisOptions(Languages.Russian), frames.Writer, cts.Token);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        WriteLine($"{result.Count} frames = {result.Count * Constants.Audio.OpusFrameDurationMs / 1000.0:F1}s");
        result.Count.Should().BeGreaterThan(50, "a sentence is more than a second of 20ms frames");
        for (var i = 0; i < result.Count; i++)
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);

        using var decoder = new OpusToPcmDecoder();
        var loudFrameCount = result.Count(f => Rms(decoder.Decode(f.Data.Span)) > 200);
        loudFrameCount.Should().BeGreaterThan(10, "speech must decode to something well above silence");
    }

    // Private methods

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSingleton(new TranscriptionSettings())
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }

    private static double Rms(byte[] pcm)
    {
        if (pcm.Length == 0)
            return 0;

        var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(pcm);
        var sum = 0.0;
        foreach (var sample in samples)
            sum += (double)sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }
}
