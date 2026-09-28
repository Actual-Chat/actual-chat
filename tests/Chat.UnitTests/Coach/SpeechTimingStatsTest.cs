namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechTimingStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    // words at 0-3, 4-7, 8-13, 14-18
    private const string Text = "one two three four";

    [Fact]
    public void ComputeShouldSubtractPausesFromSpeechTime()
    {
        // arrange: "two" ends at 2 s, "three" starts at 5 s, total 8 s
        var markup = new PlayableTextMarkup(Text, new LinearMap(0, 0, 7, 2, 8, 5, 18, 8));

        // act
        var stats = SpeechTimingStats.Compute(markup, durationSeconds: 8, minPauseSeconds: 1);

        // assert
        stats!.Pauses.Should().Be(1);
        stats.PauseSeconds.Should().BeApproximately(3, 0.01);
        stats.SpeechSeconds.Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public void ComputeShouldIgnoreGapsBelowThreshold()
    {
        // arrange
        var markup = new PlayableTextMarkup(Text, new LinearMap(0, 0, 7, 2, 8, 2.5f, 18, 6));

        // act
        var stats = SpeechTimingStats.Compute(markup, 6, 1);

        // assert
        stats!.Pauses.Should().Be(0);
        stats.SpeechSeconds.Should().BeApproximately(6, 0.01);
    }

    [Fact]
    public void DegenerateTimeMapShouldFallBackToDuration()
    {
        // arrange
        var markup = new PlayableTextMarkup(Text, LinearMap.Zero);

        // act
        var stats = SpeechTimingStats.Compute(markup, 6, 1);
        var metrics = new SpeechMetrics(6, SpeechTextStats.Compute(markup), stats);

        // assert
        stats.Should().BeNull();
        metrics.WordsPerMinute.Should().BeApproximately(40, 0.01, "4 words over 6 s of duration");
    }

    [Fact]
    public void NonMonotonicTimeMapShouldYieldNull()
        => SpeechTimingStats.Compute(new PlayableTextMarkup(Text, new LinearMap(0, 0, 7, 5, 8, 2, 18, 8)), 8, 1)
            .Should().BeNull("a time map that runs backwards cannot be trusted for pauses");

    [Fact]
    public void WordsPerMinuteShouldUseSpeechTimeWhenAvailable()
    {
        // arrange
        var markup = new PlayableTextMarkup(Text, new LinearMap(0, 0, 7, 2, 8, 5, 18, 8));

        // act
        var metrics = new SpeechMetrics(8, SpeechTextStats.Compute(markup), SpeechTimingStats.Compute(markup, 8, 1));

        // assert
        metrics.WordsPerMinute.Should().BeApproximately(48, 0.01, "4 words over 5 s of speech");
    }

    [Fact]
    public void WordsPerMinuteShouldBeNullWithoutWords()
        => new SpeechMetrics(6, null, null).WordsPerMinute.Should().BeNull();
}
