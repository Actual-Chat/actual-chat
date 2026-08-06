using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class SonioxTranscriberTest(ITestOutputHelper @out, ILogger<SonioxTranscriberTest> log)
    : TranscriberTestBase(@out, log)
{
    [Theory]
    [InlineData("196050.webm", "ru-RU")]
    [InlineData("0004-AK.webm", "ru-RU")]
    public async Task StreamingTranscribeWorks(string fileName, string languageId)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var transcriber = new SonioxTranscriber(services);
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

    [Theory]
    [InlineData("196050.webm", "ru-RU")]
    public async Task OfflineTranscribeWorks(string fileName, string languageId)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var transcriber = new SonioxOfflineTranscriber(services);
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
