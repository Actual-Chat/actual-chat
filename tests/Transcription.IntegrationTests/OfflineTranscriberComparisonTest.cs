using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

// Diagnostic: runs the same recordings through both offline transcribers and reports how far apart
// they land, so a provider swap can be judged on real audio rather than on one cherry-picked clip.

[Collection(nameof(TranscriptionCollection))]
public sealed class OfflineTranscriberComparisonTest(
    ITestOutputHelper @out,
    ILogger<OfflineTranscriberComparisonTest> log)
    : TranscriberTestBase(@out, log)
{
    private static readonly string[] Clips = ["long-ru-en-1.webm", "long-ru-en-2.webm", "long-ru-en-3.webm"];
    private const int MaxDeltas = 20;

    [Fact(Skip = "Diagnostic, for manual runs only")]
    public async Task CompareSonioxAndOpenAI()
    {
        // arrange
        var services = CreateServices();
        var settings = services.GetRequiredService<CoreServerSettings>();
        if (settings.SonioxKey.IsNullOrEmpty() || settings.OpenAIKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey or CoreSettings__OpenAIKey is not set - skipping.");
            return;
        }

        var soniox = new SonioxOfflineTranscriber(services);
        var openAI = new OpenAITranscriber(new OpenAITranscriber.Options { ApiKey = settings.OpenAIKey }, services);
        var options = new TranscriptionOptions { Language = Language.Parse("ru-RU") };

        // act
        var results = new List<(string Clip, Transcript? Soniox, Transcript? OpenAI, double Match)>();
        foreach (var clip in Clips) {
            // A fresh AudioSource per call: both transcribers drain it, so they can't share one.
            var sonioxTranscript = await soniox.Transcribe(await GetAudio(clip), options);
            var openAITranscript = await openAI.Transcribe(await GetAudio(clip), options, default);
            var match = Match(Text(sonioxTranscript), Text(openAITranscript));
            results.Add((clip, sonioxTranscript, openAITranscript, match));
        }

        // assert
        WriteLine("");
        WriteLine("clip | soniox chars | openai chars | match | soniox map | openai map");
        foreach (var (clip, sonioxTranscript, openAITranscript, match) in results)
            WriteLine($"{clip} | {Text(sonioxTranscript).Length,12} | {Text(openAITranscript).Length,12}"
                + $" | {match,6:P1} | {TimeMapInfo(sonioxTranscript),10} | {TimeMapInfo(openAITranscript),10}");

        foreach (var (clip, sonioxTranscript, openAITranscript, match) in results) {
            var sonioxText = Text(sonioxTranscript);
            var openAIText = Text(openAITranscript);
            WriteLine($"\n=== {clip} - {match:P1} match");
            WriteLine($"[soniox] {sonioxText}");
            WriteLine($"[openai] {openAIText}");
            WriteLine("--- deltas (soniox -> openai)");
            foreach (var delta in Deltas(sonioxText, openAIText).Take(MaxDeltas))
                WriteLine(delta);
        }

        results.Should().OnlyContain(x => Text(x.Soniox).Length > 0 && Text(x.OpenAI).Length > 0);
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
            .AddHttpClient()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }

    private static string Text(Transcript? transcript)
        => transcript?.Text ?? "";

    private static string TimeMapInfo(Transcript? transcript)
    {
        // A missing or degenerate map is what makes the caller DTW-remap the realtime one instead.
        if (transcript is not { } t)
            return "none";
        if (t.TimeMap.IsEmpty)
            return "empty";

        return t.TimeMap.IsDegenerate
            ? "degenerate"
            : $"{t.TimeMap.Length} pts";
    }

    private static double Match(string left, string right)
        => LinearMapDtwRemapper
            .RemapWithSimilarity(left, right, LinearMap.Zero, LinearMapAlignmentMode.RetranscribeSameAudio)
            .Similarity;

    private static IReadOnlyList<string> Deltas(string left, string right)
    {
        // Walks the same DTW alignment the similarity score uses and groups consecutive mismatched
        // tokens into spans, so the output shows differing phrases rather than a wall of single words.
        var leftTokens = LinearMapDtwRemapper.Tokenizer.TokenizeWithOffsets(left);
        var rightTokens = LinearMapDtwRemapper.Tokenizer.TokenizeWithOffsets(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return [];

        var leftSignatures = leftTokens.Select(x => LinearMapDtwRemapper.Trigram128.Signature(x.Text)).ToArray();
        var rightSignatures = rightTokens.Select(x => LinearMapDtwRemapper.Trigram128.Signature(x.Text)).ToArray();
        var rightToLeft = LinearMapDtwRemapper.Dtw.Map(leftSignatures, rightSignatures, 64);

        var deltas = new List<string>();
        var j = 0;
        while (j < rightTokens.Count) {
            if (IsMatch(j)) {
                j++;
                continue;
            }

            var start = j;
            while (j < rightTokens.Count && !IsMatch(j))
                j++;

            var leftFrom = rightToLeft[start];
            var leftTo = j < rightTokens.Count ? rightToLeft[j] : leftTokens.Count;
            var leftSpan = leftFrom >= leftTo
                ? ""
                : leftTokens.Skip(leftFrom).Take(leftTo - leftFrom).Select(x => x.Text).ToDelimitedString(" ");
            var rightSpan = rightTokens.Skip(start).Take(j - start).Select(x => x.Text).ToDelimitedString(" ");
            deltas.Add($"  '{leftSpan}' -> '{rightSpan}'");
        }

        return deltas;

        bool IsMatch(int index) {
            var i = rightToLeft[index];
            return i >= 0 && i < leftTokens.Count && leftTokens[i].Text == rightTokens[index].Text;
        }
    }
}
