using ActualChat.Module;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Transcription.IntegrationTests;

// Diagnostic: Google is the fallback for the languages Soniox doesn't cover, and chirp_2 documents
// StreamingRecognize as supporting far fewer languages than the model itself. This asks the API
// which of ours it will actually take on the streaming path - the audio is Russian throughout, so
// only acceptance is under test, not the transcript.

[Collection(nameof(TranscriptionCollection))]
public sealed class GoogleLanguageSupportTest(
    IConfiguration configuration,
    ITestOutputHelper @out,
    ILogger<GoogleLanguageSupportTest> log
    ) : TranscriberTestBase(@out, log)
{
    private const string Clip = "long-ru-en-1.webm";
    private CoreServerSettings CoreServerSettings { get; }
        = configuration.Settings<CoreServerSettings>(nameof(CoreSettings));

    [Theory(Skip = "Diagnostic, for manual runs only")]
    [InlineData("ru-RU")]
    [InlineData("uz-UZ")]
    [InlineData("quz-PE")] // Dropped from Languages.All - kept here to re-check if it ever lands
    public async Task StreamingAcceptsLanguage(string languageCode)
    {
        // arrange
        // Google Speech v2 doesn't work over HTTP/3.
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => CoreServerSettings)
            .AddTestLogging(Out)
            .BuildServiceProvider();
        var transcriber = new GoogleTranscriber(services);
        var options = new TranscriptionOptions { Language = Language.Parse(languageCode) };
        var audio = await GetAudio(Clip, withDelay: false);

        // act
        var text = "";
        Exception? error = null;
        try {
            await foreach (var transcript in transcriber.Transcribe("test", audio, options))
                text = transcript.Text;
        }
        catch (Exception e) {
            error = e;
        }

        // assert
        if (error != null)
            WriteLine($"{languageCode}: REJECTED - {error.GetType().Name}: {error.Message}");
        else
            WriteLine($"{languageCode}: accepted, {text.Length} chars - {text.Truncate(120)}");
    }
}
