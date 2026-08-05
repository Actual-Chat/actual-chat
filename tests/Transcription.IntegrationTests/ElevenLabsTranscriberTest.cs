using ActualChat.Hosting;
using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public sealed class ElevenLabsTranscriberTest(ITestOutputHelper @out, ILogger<ElevenLabsTranscriberTest> log)
    : TranscriberTestBase(@out, log)
{
    [Theory(Skip = "For manual runs only")]
    [InlineData("196050.webm", "ru-RU")]
    [InlineData("0004-AK.webm", "ru-RU")]
    public async Task StreamingTranscribeWorks(string fileName, string languageId)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().ElevenLabsKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__ElevenLabsKey is not set - skipping.");
            return;
        }

        var transcriber = new ElevenLabsTranscriber(services);
        var options = new TranscriptionOptions { Language = Language.Parse(languageId) };
        var audio = await GetAudio(fileName, withDelay: true);

        // act
        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();

        // assert
        WriteLine($"{transcripts.Count} transcripts");
        foreach (var t in transcripts.TakeLast(3))
            WriteLine(t.ToString());
        transcripts.Should().NotBeEmpty();
        transcripts[^1].Text.Should().NotBeNullOrWhiteSpace();
    }

    [Theory(Skip = "For manual runs only")]
    [InlineData("196050.webm", "ru-RU")]
    public async Task OfflineTranscribeWorks(string fileName, string languageId)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().ElevenLabsKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__ElevenLabsKey is not set - skipping.");
            return;
        }

        var transcriber = new ElevenLabsOfflineTranscriber(services);
        var options = new TranscriptionOptions { Language = Language.Parse(languageId) };
        var audio = await GetAudio(fileName);

        // act
        var transcript = await transcriber.Transcribe(audio, options);

        // assert
        WriteLine(transcript?.ToString() ?? "<null>");
        transcript.Should().NotBeNull();
        transcript!.Text.Should().NotBeNullOrWhiteSpace();
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
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
