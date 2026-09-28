using System.Text.Json;
using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

/// <summary>
/// Gate for prompt edits: the real tagger against a hand-tagged golden set. Local only.
/// </summary>
[Collection(nameof(ChatCollection))]
public class SpeechTaggerGoldenTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatCollection.AppHostFixture>(fixture, @out)
{
    private sealed record Golden(string Language, string Text, GoldenItem[] Expected);
    private sealed record GoldenItem(string Class, string Word, int Occurrence);

    [LocalFact("Needs CoreSettings__OpenAIKey and CoreSettings__PromptsDir; gates prompt edits")]
    public async Task TaggerShouldMatchGoldenSetWithinTolerance()
    {
        // arrange
        await using var appHost = await NewAppHost("coach-golden", options => options with {
            ConfigureHost = (_, cfg) => cfg.AddInMemoryCollection(
                ($"{nameof(ChatSettings)}:{nameof(ChatSettings.Coach)}:{nameof(CoachSettings.IsEnabled)}", "true")),
        });
        var tagger = appHost.Services.GetRequiredService<ISpeechTagger>();
        tagger.Should().BeOfType<SpeechTagger>("the real tagger needs CoreSettings__OpenAIKey");
        var json = await File.ReadAllTextAsync(Path.Combine("data", "coach-golden.json"));
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var goldens = JsonSerializer.Deserialize<Golden[]>(json, jsonOptions)!;
        var hits = 0;
        var total = 0;
        var extras = 0;

        // act
        foreach (var golden in goldens) {
            var result = await tagger.Tag(new SpeechTagRequest(golden.Text, Language.Parse(golden.Language)), default);
            result.Should().NotBeNull("a failed call means the prompt or the key is broken");
            var got = result!.Spans.Select(s => (Kind: s.Kind.ToString(), s.Word, s.Start)).ToHashSet();
            foreach (var item in golden.Expected) {
                total++;
                var range = SpanLocator.Locate(golden.Text, item.Word, item.Occurrence)!.Value;
                var kind = item.Class switch {
                    "filledPause" => nameof(SpeechSpanKind.FilledPause),
                    "filler" => nameof(SpeechSpanKind.Filler),
                    "weak" => nameof(SpeechSpanKind.Weak),
                    _ => nameof(SpeechSpanKind.Profanity),
                };
                if (got.Contains((kind, item.Word, range.Start)))
                    hits++;
            }
            extras += Math.Max(0, result.Spans.Count - golden.Expected.Length);
            Out.WriteLine($"{golden.Language}: {result.Spans.Count} spans, expected {golden.Expected.Length}: "
                + string.Join(", ", result.Spans.Select(s => $"{s.Kind}:{s.Word}@{s.Start}")));
        }

        // assert
        (hits / (double)total).Should().BeGreaterThanOrEqualTo(0.8, "recall on the golden set");
        extras.Should().BeLessThanOrEqualTo(total / 2, "over-tagging");
    }
}
