using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachScoringTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings S = new();

    private static CoachDay DayWith(
        int words, int fillers = 0, int weak = 0, double speechSeconds = 100, int sentences = 10)
        => new CoachDay(Day) {
            Entries = 1,
            TaggedEntries = 1,
            Words = words,
            TaggedWords = words,
            Fillers = fillers,
            WeakWords = weak,
            SpeechSeconds = speechSeconds,
            DurationSeconds = speechSeconds,
            Sentences = sentences,
        };

    [Theory]
    [InlineData(0.03, 100)]
    [InlineData(0.06, 50)]
    [InlineData(0.09, 0)]
    [InlineData(0.20, 0)]
    public void SubScoreShouldFallLinearlyToZeroAtTwiceTheEdgeDistance(double rate, double expected)
        => CoachScoring.SubScore(rate, 0, S.FillerGoodRate).Should().BeApproximately(expected, 0.01);

    [Theory]
    [InlineData(135, 100)]
    [InlineData(160, 100)]
    [InlineData(185, 50)]
    [InlineData(210, 0)]
    [InlineData(85, 50)]
    public void TwoSidedSubScoreShouldUseTheBandWidthAsTheFallOff(double wpm, double expected)
        => CoachScoring.SubScore(wpm, S.PaceSlowWpm, S.PaceFastWpm).Should().BeApproximately(expected, 0.01);

    [Fact]
    public void ScoreShouldBeAbsentBelowMinWords()
    {
        // act
        var summary = CoachScoring.Summarize(CoachWindow.Today, DayWith(199), null, S);

        // assert
        summary.Score.Should().BeNull();
        summary.ScoreDelta.Should().BeNull();
        summary.Metrics.Should().Contain(m => m.Kind == CoachMetricKind.Pace && m.Value != null,
            "metric rows still show");
    }

    [Fact]
    public void ScoreShouldBe100ForASpeakerInsideEveryBand()
    {
        // arrange: 300 words in 120 s = 150 wpm, 2% fillers, 2% weak, 15 words/sentence, no runs
        var day = DayWith(300, fillers: 6, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var score = CoachScoring.Score(day, S, null);

        // assert
        score.Should().Be(100, "turn-taking has no data and its weight is redistributed");
    }

    [Fact]
    public void ScoreShouldWeightFillersAt30()
    {
        // arrange: fillers at 9% give a zero sub-score; everything else is perfect; turn-taking absent
        var day = DayWith(300, fillers: 27, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var score = CoachScoring.Score(day, S, null);

        // assert
        score.Should().Be(65, "weights without turn-taking: fillers 30, pace 25, weak 20, sentence 10; 55/85");
    }

    [Fact]
    public void BadgeShouldNeedAtLeastThreePoints()
    {
        // arrange
        var now = DayWith(300, fillers: 6, weak: 6, speechSeconds: 120, sentences: 20);
        var trailing = DayWith(300, fillers: 18, weak: 6, speechSeconds: 120, sentences: 20);

        // act
        var summary = CoachScoring.Summarize(CoachWindow.Week, now, trailing, S);

        // assert
        summary.Score.Should().Be(100);
        summary.ScoreDelta.Should().BeGreaterThanOrEqualTo(S.BadgeMinDelta);
        CoachScoring.Summarize(CoachWindow.Week, now, now, S).ScoreDelta.Should().BeNull("no change is no badge");
    }

    [Fact]
    public void ChipsShouldListTopWordsByCount()
    {
        // arrange
        var day = DayWith(300, fillers: 12) with {
            FillerCounts = new ApiMap<string, int>(
                new Dictionary<string, int> { ["you know"] = 7, ["like"] = 4, ["um"] = 1 }),
        };

        // act
        var fillers = CoachScoring.Summarize(CoachWindow.Today, day, null, S)
            .Metrics.Single(m => m.Kind == CoachMetricKind.Fillers);

        // assert
        fillers.Chips.Select(c => c.Word).Should().Equal("you know", "like", "um");
        fillers.Band.Should().Be(CoachBand.Medium, "12/300 = 4%");
    }

    [Fact]
    public void TurnTakingShouldBeJudgedAgainstFairShare()
    {
        // arrange: own 10 s of 100 s with 2 participants, so fair share is 50 s and the ratio 0.2
        var day = DayWith(300) with {
            Runs = 1, OwnSpeechSeconds = 10, TotalSpeechSeconds = 100, FairShareSeconds = 50,
        };

        // act
        var turn = CoachScoring.Summarize(CoachWindow.Today, day, null, S)
            .Metrics.Single(m => m.Kind == CoachMetricKind.TurnTaking);

        // assert
        turn.Value.Should().BeApproximately(0.1, 0.001, "the value is the raw share");
        turn.Band.Should().Be(CoachBand.Low);
    }

    [Fact]
    public void PaceBandShouldUseTheLanguageOverride()
    {
        // arrange
        var s = new CoachScoringSettings { PaceByLanguage = { ["ru"] = new PaceBand { Slow = 90, Fast = 140 } } };

        // assert
        CoachScoring.PaceBand(150, s, "ru-RU").Should().Be(CoachBand.High);
        CoachScoring.PaceBand(150, s, "en-US").Should().Be(CoachBand.Good);
    }

    private static CoachDay ScoredDay(
        int words, int fillers, int weak, double speechSeconds, int sentences, double ownSpeech, double fairShare)
        => DayWith(words, fillers, weak, speechSeconds, sentences) with {
            OwnSpeechSeconds = ownSpeech,
            FairShareSeconds = fairShare,
            TotalSpeechSeconds = ownSpeech * 3,
            Runs = ownSpeech > 0 ? 1 : 0,
        };

    [Fact]
    public void ExplainShouldAddUpToTheScore()
    {
        // arrange
        var day = ScoredDay(1000, 50, 30, 400, 100, 100, 100);

        // act
        var parts = CoachScoring.Explain(day, S, "en-US");
        var score = CoachScoring.Score(day, S, "en-US");

        // assert
        parts.Select(p => p.Kind).Should().BeEquivalentTo(
            [CoachMetricKind.Fillers, CoachMetricKind.Pace, CoachMetricKind.WeakWords, CoachMetricKind.TurnTaking,
                CoachMetricKind.SentenceLength]);
        parts.Sum(p => p.MaxPoints).Should().BeApproximately(100, 1e-9);
        ((int)Math.Round(parts.Sum(p => p.Points))).Should().Be(score!.Value);
        parts.Single(p => p.Kind == CoachMetricKind.Fillers).Band.Should().Be(CoachBand.Medium);
    }

    [Fact]
    public void ExplainShouldSpreadWeightsWhenAnInputIsMissing()
    {
        // arrange: no runs, so no turn-taking
        var day = ScoredDay(1000, 10, 10, 400, 100, 0, 0);

        // act
        var parts = CoachScoring.Explain(day, S, "en-US");

        // assert
        parts.Should().NotContain(p => p.Kind == CoachMetricKind.TurnTaking);
        parts.Sum(p => p.MaxPoints).Should().BeApproximately(100, 1e-9);
    }

    [Fact]
    public void FillerBandShouldFollowTheLanguageTable()
    {
        // arrange
        var s = new CoachScoringSettings {
            FillerByLanguage = { ["ru"] = new RateBand { Good = 0.05, High = 0.10 } },
        };
        var day = ScoredDay(1000, 40, 0, 400, 100, 0, 0);

        // act
        var ru = CoachScoring.Summarize(CoachWindow.AllTime, day, null, s, "ru-RU").Metrics
            .Single(m => m.Kind == CoachMetricKind.Fillers).Band;
        var en = CoachScoring.Summarize(CoachWindow.AllTime, day, null, s, "en-US").Metrics
            .Single(m => m.Kind == CoachMetricKind.Fillers).Band;

        // assert
        ru.Should().Be(CoachBand.Good);
        en.Should().Be(CoachBand.Medium);
    }
}
