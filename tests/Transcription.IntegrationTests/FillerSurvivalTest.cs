using ActualChat.Module;
using ActualChat.Transcription.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

/// <summary>
/// Speech-coach spike: which filler words survive Soniox realtime vs offline transcription.
/// Diagnostic; prints counts and fails only when the pipeline returns nothing.
/// </summary>
[Collection(nameof(TranscriptionCollection))]
public class FillerSurvivalTest(ITestOutputHelper @out, ILogger<FillerSurvivalTest> log)
    : TranscriberTestBase(@out, log)
{
    [LocalTheory("Requires CoreSettings__SonioxKey and real audio; diagnostic only")]
    [InlineData("fillers-en.webm", "en-US", "um,uh,you know,like")]
    [InlineData("fillers-ru.webm", "ru-RU", "эээ,ну,вот,как бы")]
    public async Task SonioxShouldReportWhichFillersSurvive(string file, string language, string fillers)
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }
        var options = new TranscriptionOptions { Language = Language.Parse(language) };
        var expected = fillers.Split(',');

        // act
        var realtime = new SonioxTranscriber(services);
        var realtimeTranscripts = await realtime
            .Transcribe("spike", await GetAudio(file, withDelay: true), options)
            .ToListAsync();
        var realtimeText = realtimeTranscripts[^1].Text;

        var offline = new SonioxOfflineTranscriber(services);
        var cleaner = services.GetRequiredService<SonioxCleaner>();
        await RequireSonioxCapacity(services.GetRequiredService<SonioxClient>(), file);
        var offlineText = (await offline.Transcribe(await GetAudio(file), options))?.Text ?? "";
        await cleaner.Flush();

        // assert
        WriteLine($"REALTIME: {realtimeText}");
        WriteLine($"OFFLINE:  {offlineText}");
        foreach (var f in expected)
            WriteLine($"{f,-10} realtime={Count(realtimeText, f)} offline={Count(offlineText, f)}");
        realtimeText.Should().NotBeEmpty();
        offlineText.Should().NotBeEmpty();
    }

    // Private methods

    private static int Count(string text, string phrase)
    {
        var normalized = string.Join(' ', text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(',', '.', '!', '?', ';', ':', '"', '«', '»').ToLower()));
        var count = 0;
        var i = 0;
        while ((i = normalized.IndexOf(phrase, i, StringComparison.OrdinalIgnoreCase)) >= 0) {
            var isWholeWord = (i == 0 || normalized[i - 1] == ' ')
                && (i + phrase.Length == normalized.Length || normalized[i + phrase.Length] == ' ');
            if (isWholeWord)
                count++;
            i += phrase.Length;
        }
        return count;
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
            .AddSingleton(new TranscriptionSettings())
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
