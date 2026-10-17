using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public sealed class XaiTranscriberTest(ITestOutputHelper @out, ILogger<XaiTranscriberTest> log)
    : TranscriberTestBase(@out, log)
{
    [Theory(Skip = "For manual runs only")]
    [InlineData("196050.webm", "ru-RU", false)]
    [InlineData("0004-AK.webm", "ru-RU", false)]
    [InlineData("196050.webm", "ru-RU", true)]
    [InlineData("long-ru-en-1.webm", "ru-RU", true)]
    [InlineData("pauses.webm", "ru-RU", false)]
    public async Task StreamingTranscribeWorks(string fileName, string languageId, bool detectLanguage)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().XaiApiKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__XaiApiKey is not set - skipping.");
            return;
        }

        var transcriber = new XaiTranscriber(services);
        var language = Language.Parse(languageId);
        var options = new TranscriptionOptions {
            Language = language,
            DetectLanguage = detectLanguage,
            LanguageCandidates = detectLanguage ? [language, Languages.English] : [],
        };
        var audio = await GetAudio(fileName, withDelay: true);

        // act
        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();

        // assert
        WriteLine($"{transcripts.Count} transcripts");
        foreach (var t in transcripts.TakeLast(3))
            WriteLine(t.ToString());
        transcripts.Should().NotBeEmpty();
        transcripts[^1].Text.Should().NotBeNullOrWhiteSpace();
        transcripts[^1].IsStable.Should().BeTrue();
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
