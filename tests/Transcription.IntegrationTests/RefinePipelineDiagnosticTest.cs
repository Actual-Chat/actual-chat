using ActualChat.Chat.ML;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Transcription;
using Microsoft.Extensions.Configuration;
using ActualLab.IO;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class RefinePipelineDiagnosticTest(
    IConfiguration configuration,
    ITestOutputHelper @out,
    ILogger<RefinePipelineDiagnosticTest> log
    ) : TranscriberTestBase(@out, log)
{
    private CoreServerSettings CoreServerSettings { get; }
        = configuration.Settings<CoreServerSettings>(nameof(CoreSettings));

    // Manual diagnostic: drop a problematic recording into data/, add an [InlineData] line with the
    // chat language, then run. It replays the same audio through the realtime+refine pipeline used in
    // chat-language mode (Google realtime → OpenAI refine) and prints each stage's text so we can see
    // where a wrong language or hallucination ("Субтитры создал …") is introduced.
    // Requires: Google credentials (CoreSettings / GOOGLE_APPLICATION_CREDENTIALS) and an OpenAI key
    // (CoreSettings__OpenAIKey).
    [Theory(Skip = "For manual runs only")]
    [InlineData("196050.webm", "ru-RU")]
    [InlineData("195977.webm", "ru-RU")]
    public async Task DiagnoseRefinePipeline(string fileName, string languageCode)
    {
        // Google Speech v2 doesn't work over HTTP/3.
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = CreateServices();
        var language = Language.Parse(languageCode);

        var realtime = await Stage("realtime/Google (chat language)",
            () => RunGoogle(services, fileName, new TranscriptionOptions { Language = language }));
        var refined = await Stage("refine/OpenAI gpt-4o-transcribe (forced language)",
            () => RunOpenAIRefine(services, fileName, new TranscriptionOptions { Language = language }));

        var realtimeText = realtime?.Text ?? "";
        var refinedText = refined?.Text ?? "";
        var useOriginal = realtimeText.ShouldUseOriginalTranscript(refinedText);
        var finalText = refined is not null && !useOriginal ? refinedText : realtimeText;

        WriteLine("");
        WriteLine("================ Refine pipeline diagnostic ================");
        WriteLine($"File: {fileName}   chat language: {language}");
        WriteLine("");
        WriteLine($"[1] realtime/Google  languages=[{Langs(realtime)}]  len={realtimeText.Length}");
        WriteLine($"      {realtimeText}");
        WriteLine($"[2] refine/OpenAI    len={refinedText.Length}");
        WriteLine($"      {refinedText}");
        WriteLine("");
        WriteLine($"ShouldUseOriginalTranscript = {useOriginal}  ->  FINAL uses {(useOriginal ? "[1] REALTIME" : "[2] REFINE")}");
        WriteLine($"FINAL: {finalText}");
        WriteLine("============================================================");
    }

    [Theory(Skip = "For manual runs only")]
    [InlineData("196050.webm", "ru-RU")]
    [InlineData("195977.webm", "ru-RU")]
    public async Task CompareRefineModels(string fileName, string languageCode)
    {
        // Google Speech v2 doesn't work over HTTP/3.
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = CreateServices();
        var language = Language.Parse(languageCode);
        const int runs = 3;
        var models = new[] {
            "gpt-4o-transcribe",
            "gpt-4o-mini-transcribe-2025-12-15",
            "gpt-4o-mini-transcribe-2025-03-20",
            "whisper-1",
        };

        var realtime = await Stage("realtime/Google",
            () => RunGoogle(services, fileName, new TranscriptionOptions { Language = language }));

        WriteLine("");
        WriteLine($"======== Model comparison: {fileName} ({language}) ========");
        WriteLine($"realtime/Google (reference): {realtime?.Text}   [{ScriptTag(realtime?.Text)}]");
        foreach (var model in models) {
            WriteLine($"-- {model} --");
            for (var i = 1; i <= runs; i++) {
                var r = await Stage($"{model} #{i}",
                    () => RunOpenAIRefine(services, fileName, new TranscriptionOptions { Language = language }, model));
                WriteLine($"   run{i}: {r?.Text}   [{ScriptTag(r?.Text)}]");
            }
        }
        WriteLine("========================================================");
    }

    // Private methods

    private async Task<Transcript?> RunGoogle(IServiceProvider services, FilePath fileName, TranscriptionOptions options)
    {
        var transcriber = new GoogleTranscriber(services);
        var audio = await GetAudio(fileName, withDelay: true);
        var transcripts = await transcriber.Transcribe("rt", audio, options, CancellationToken.None).ToListAsync();
        return transcripts.Count > 0 ? transcripts[^1] : null;
    }

    private async Task<Transcript?> RunOpenAIRefine(IServiceProvider services, FilePath fileName, TranscriptionOptions options, string? model = null)
    {
        var apiKey = configuration["CoreSettings:OpenAIKey"] ?? "";
        model = model.NullIfEmpty() ?? OpenAITranscriber.DefaultModel;
        var transcriber = new OpenAITranscriber(new OpenAITranscriber.Options { ApiKey = apiKey, Model = model }, services);
        var audio = await GetAudio(fileName, withDelay: false);
        return await transcriber.Transcribe(audio, options, CancellationToken.None);
    }

    private async Task<Transcript?> Stage(string name, Func<Task<Transcript?>> run)
    {
        try {
            return await run().ConfigureAwait(false);
        }
        catch (Exception e) {
            WriteLine($"[stage failed] {name}: {e.Message}");
            return null;
        }
    }

    private static string Langs(Transcript? transcript)
        => transcript is null ? "" : string.Join(",", transcript.Languages.Select(l => l.Value));

    private static string ScriptTag(string? text)
    {
        if (text.IsNullOrEmpty())
            return "empty";

        var letters = 0;
        var cyrillic = 0;
        foreach (var ch in text) {
            if (!char.IsLetter(ch))
                continue;
            letters++;
            if (ch is >= 'Ѐ' and <= 'ӿ')
                cyrillic++;
        }
        if (letters == 0)
            return "no-letters";

        return cyrillic * 100 / letters >= 60 ? "Cyrillic" : "non-Cyrillic";
    }

    private IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(CoreServerSettings)
            .AddSingleton(MomentClockSet.Default)
            .AddTestLogging(Out)
            .BuildServiceProvider();
}
