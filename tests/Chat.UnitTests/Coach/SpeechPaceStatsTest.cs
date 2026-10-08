namespace ActualChat.Chat.UnitTests.Coach;

public sealed class SpeechPaceStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData("word", "en")]
    [InlineData("слово", "ru")]
    public void DetailedTimingShouldUseExistingWordCounts(string word, string iso)
    {
        // arrange
        var markup = Markup(word, 50, 0.4f);

        // act
        var result = SpeechPaceStats.Compute(markup, 20, Language.Parse(iso), hasDetailedTiming: true);
        var text = SpeechTextStats.Compute(markup);

        // assert
        result.Should().NotBeNull();
        result!.ValidWords.Should().Be(text!.Words);
        result.Segments.Should().HaveCount(2);
        result.Segments.Should().OnlyContain(x => x.WordsPerMinute == 150);
    }

    [Fact]
    public void UnknownTimingProvenanceShouldNotClaimDetailedMeasurements()
    {
        // arrange
        var markup = Markup("word", 25, 0.4f);

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.English, hasDetailedTiming: false);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void CoarseWholeRecordingMapShouldNotProduceLocalPace()
    {
        // arrange
        var text = string.Join(" ", Enumerable.Repeat("word", 25));
        var markup = new PlayableTextMarkup(text, new LinearMap(0, 0, text.Length, 10));

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.English, hasDetailedTiming: true);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void DegenerateMapShouldNotFallBackToRecordingDuration()
    {
        // arrange
        var markup = new PlayableTextMarkup("one two three four five", LinearMap.Zero);

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.English, hasDetailedTiming: true);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void UnsupportedWordSegmentationShouldRemainUnavailable()
    {
        // arrange
        var markup = Markup("你好", 25, 0.4f);

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.Chinese, hasDetailedTiming: true);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void PunctuationShouldUseTheSameLexicalPolicyAsHeadlineMetrics()
    {
        // arrange
        var markup = Markup("«word!»", 25, 0.4f);

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.English, hasDetailedTiming: true);
        var text = SpeechTextStats.Compute(markup);

        // assert
        result!.ValidWords.Should().Be(text!.Words).And.Be(25);
        result.Segments[0].TextRange.Start.Should().Be(1);
        result.Segments[0].TextRange.End.Should().Be(markup.Text.Length - 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRecordingDurationShouldRemainUnavailable(double duration)
    {
        // arrange
        var markup = Markup("word", 25, 0.4f);

        // act
        var result = SpeechPaceStats.Compute(markup, duration, Languages.English, hasDetailedTiming: true);

        // assert
        result.Should().BeNull();
    }

    [Fact]
    public void LeadingAndTrailingSilenceShouldBeReportedAsUnmapped()
    {
        // arrange
        var markup = Markup("word", 25, 0.4f, 2);

        // act
        var result = SpeechPaceStats.Compute(markup, 14, Languages.English, hasDetailedTiming: true);

        // assert
        result!.Segments.Should().ContainSingle();
        result.Segments[0].TimeRange.Start.Should().Be(2_000);
        result.UnmappedMilliseconds.Should().Be(4_000);
    }

    [Fact]
    public void InterpolatedWordStartsShouldNotBeTreatedAsExactTiming()
    {
        // arrange
        var original = Markup("word", 25, 0.4f);
        var points = Enumerable.Range(0, original.TimeMap.Length)
            .Where(i => i % 2 == 1)
            .SelectMany(i => new[] { original.TimeMap[i].X, original.TimeMap[i].Y }).ToArray();
        var markup = new PlayableTextMarkup(original.Text, new LinearMap(points));

        // act
        var result = SpeechPaceStats.Compute(markup, 10, Languages.English, hasDetailedTiming: true);

        // assert
        result!.ValidWords.Should().Be(0);
        result.RejectedWords.Should().Be(25);
        result.Segments.Should().BeEmpty();
    }

    // Private methods

    private static PlayableTextMarkup Markup(string word, int count, float seconds, float start = 0)
    {
        var text = string.Join(" ", Enumerable.Repeat(word, count));
        var points = new List<float>();
        for (var i = 0; i < count; i++) {
            var offset = i * (word.Length + 1);
            points.Add(offset);
            points.Add(start + i * seconds);
            points.Add(offset + word.Length);
            points.Add(start + (i + 1) * seconds);
        }
        return new PlayableTextMarkup(text, new LinearMap(points.ToArray()));
    }
}
