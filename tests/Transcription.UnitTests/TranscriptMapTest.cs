using ActualChat.Chat;

namespace ActualChat.Transcription.UnitTests;

public class TranscriptMapTest
{
    [Fact]
    public void CompletionShouldCarryPreciseMapThroughDiffs()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(TimeSpan.FromSeconds(1));
        var tokens = Enumerable.Range(0, 25).Select(i => new SonioxToken {
            Text = i == 0 ? "word" : " word",
            StartMs = 2_000 + i * 400 + (i >= 12 ? 2_000 : 0),
            EndMs = 2_400 + i * 400 + (i >= 12 ? 2_000 : 0),
            IsFinal = true,
            Language = "en",
        }).ToArray();
        var stable = builder.Update(tokens, 14_000).Single();

        // act
        var complete = builder.Complete();
        var folded = Transcript.Empty + (stable - Transcript.Empty) + (complete - stable);
        var markup = new PlayableTextMarkup(folded.Text, folded.TimeMap);
        var pace = SpeechPaceStats.Compute(markup, 16, Languages.English, hasDetailedTiming: true);

        // assert
        pace!.PauseMilliseconds.Should().Be(2_000);
        pace.UnmappedMilliseconds.Should().Be(4_000);
        pace.Segments.Should().OnlyContain(x => x.WordsPerMinute == 150);
        folded.TimeMap.IsIdenticalTo(complete.TimeMap).Should().BeTrue();
    }

    [Fact]
    public void DiffsShouldPreserveChangesBelowPlaybackEpsilon()
    {
        // arrange
        var originalMap = new LinearMap(0, 0.1f, 5, 0.5f, 6, 0.7f, 11, 1.2f);
        var preciseMap = new LinearMap(0, 0.12f, 5, 0.52f, 6, 0.72f, 11, 1.22f);
        var original = new Transcript("hello world", originalMap, [Languages.English]) { IsStable = true };
        var complete = original with { TimeMap = preciseMap };

        // act
        var folded = original + (complete - original);

        // assert
        folded.TimeMap.IsIdenticalTo(preciseMap).Should().BeTrue();
    }

    [Fact]
    public void MixedSpeechShouldUseTheUtteranceLanguage()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(TimeSpan.FromSeconds(1));
        builder.Update([
            new SonioxToken { Text = "hello", StartMs = 100, EndMs = 500, IsFinal = true, Language = "en" },
            new SonioxToken { Text = " мир", StartMs = 600, EndMs = 1_000, IsFinal = true, Language = "ru" },
        ], 2_000);

        // act
        var complete = builder.Complete();
        var markup = new PlayableTextMarkup(complete.Text, complete.TimeMap);
        var pace = SpeechPaceStats.Compute(markup, 2, Languages.English, hasDetailedTiming: true);

        // assert
        complete.Languages.Should().Equal(Languages.English, Languages.Russian);
        pace!.ValidWords.Should().Be(2);
    }

    [Fact]
    public void CompletionShouldNotReadMutatedProviderTokens()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(TimeSpan.FromSeconds(1));
        var token = new SonioxToken {
            Text = "hello world", StartMs = 100, EndMs = 1_500, IsFinal = true, Language = "en",
        };
        builder.Update([token], 2_000);
        token.Text = "changed";
        token.StartMs = 700;
        token.EndMs = 900;

        // act
        var complete = builder.Complete();

        // assert
        complete.Text.Should().Be("hello world");
        complete.TimeMap.IsIdenticalTo(new LinearMap(0, 0.1f, 11, 1.5f)).Should().BeTrue();
    }

    [Fact]
    public void HistoricalMapShouldRoundTripWithoutNewFields()
    {
        // arrange
        var original = new Transcript("hello world", new LinearMap(0, 0, 11, 1.2f), [Languages.English]);

        // act
        var json = SystemJsonSerializer.Default.Write(original);
        var restored = SystemJsonSerializer.Default.Read<Transcript>(json);

        // assert
        restored.Text.Should().Be(original.Text);
        restored.TimeMap.IsIdenticalTo(original.TimeMap).Should().BeTrue();
        json.Should().NotContain("Timing");
    }

    [Fact]
    public void InvalidDetailedTimingShouldNotBreakPlaybackTranscript()
    {
        // arrange
        var builder = new SonioxTranscriptBuilder(TimeSpan.FromSeconds(1));
        builder.Update([
            new SonioxToken { Text = "hello", StartMs = 1_000, EndMs = 1_500, IsFinal = true },
            new SonioxToken { Text = " world", StartMs = 1_200, EndMs = 1_800, IsFinal = true },
        ], 2_000);

        // act
        var complete = builder.Complete();

        // assert
        complete.Text.Should().Be("hello world");
        complete.TimeMap.IsValid().Should().BeTrue();
    }
}
