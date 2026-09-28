using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachTipPolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings S = new();
    private static readonly UserCoachSettings OptedIn = new() { IsCoachingEnabled = true, AreLiveTipsEnabled = true };
    private static readonly UserCoachTip NoTip = new();
    private static readonly ApiArray<SpeechSpan> NoSpans = ApiArray<SpeechSpan>.Empty;

    private static CoachRecord Entry(int words, double speechSeconds, int fillers = 0, int weak = 0)
        => new (CoachRecordKind.Entry, "e1", UserId.New(), GroupChatId.New(), Now) {
            Entry = new CoachEntryRecord(1, "en-US", speechSeconds, speechSeconds, words, 2, 0, 0, words, 0, 0, true,
                0, fillers, weak, 0, NoSpans),
        };

    private static CoachDay Fillers(int youKnow)
        => new CoachDay(Day) {
            FillerCounts = new ApiMap<string, int>(new Dictionary<string, int> { ["you know"] = youKnow }),
        };

    private static CoachDay Weak(int awesome)
        => new CoachDay(Day) {
            WeakWordCounts = new ApiMap<string, int>(new Dictionary<string, int> { ["awesome"] = awesome }),
        };

    private static UserCoachTip? Evaluate(
        CoachRecord record, CoachDay before, CoachDay after, ApiArray<SpeechSpan> spans, UserCoachTip previous,
        UserCoachSettings? settings = null)
        => CoachTipPolicy.Evaluate(record, before, after, spans, previous, settings ?? OptedIn, S, Now, "en-US");

    [Fact]
    public void FillerCountCrossingTenShouldTip()
    {
        // act
        var tip = Evaluate(Entry(40, 20), Fillers(9), Fillers(11), NoSpans, NoTip);

        // assert
        tip!.Kind.Should().Be(CoachTipKind.Filler);
        tip.Word.Should().Be("you know");
        tip.Count.Should().Be(11);
        tip.LastTipAt.Should().Be(Now);
        tip.IsDismissed.Should().BeFalse();
    }

    [Fact]
    public void FillerCountStayingBetweenStepsShouldNotTip()
        => Evaluate(Entry(40, 20), Fillers(11), Fillers(13), NoSpans, NoTip).Should().BeNull();

    [Fact]
    public void WeakWordShouldTipOnlyWithSynonyms()
    {
        // arrange
        var synonyms = ApiArray.New("excellent", "superb");
        var spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Weak, "awesome", 0, 7, synonyms));

        // act
        var with = Evaluate(Entry(40, 20, weak: 1), Weak(9), Weak(10), spans, NoTip);
        var without = Evaluate(Entry(40, 20, weak: 1), Weak(9), Weak(10), NoSpans, NoTip);

        // assert
        with!.Kind.Should().Be(CoachTipKind.WeakWord);
        with.Synonyms.Should().Equal("excellent", "superb");
        without.Should().BeNull();
    }

    [Theory]
    [InlineData(60, 20, CoachTipKind.SlowDown)]
    [InlineData(30, 20, CoachTipKind.SpeedUp)]
    [InlineData(50, 20, CoachTipKind.None)]
    [InlineData(20, 5, CoachTipKind.None)]
    public void PaceTipShouldNeedEnoughWordsAndAnOutOfBandRate(int words, double seconds, CoachTipKind expected)
    {
        // act
        var tip = Evaluate(Entry(words, seconds), new CoachDay(Day), new CoachDay(Day), NoSpans, NoTip);

        // assert
        (tip?.Kind ?? CoachTipKind.None).Should().Be(expected);
        if (tip is not null)
            tip.Wpm.Should().Be((int)Math.Round(words * 60 / seconds));
    }

    [Fact]
    public void WordTipShouldWinOverPace()
        => Evaluate(Entry(60, 20), Fillers(9), Fillers(10), NoSpans, NoTip)!.Kind.Should().Be(CoachTipKind.Filler);

    [Fact]
    public void TipsShouldRespectTheInterval()
    {
        // arrange
        var recent = new UserCoachTip {
            Kind = CoachTipKind.Filler, LastTipAt = Now - TimeSpan.FromMinutes(2), IsDismissed = true,
        };

        // act
        var tip = Evaluate(Entry(60, 20), Fillers(9), Fillers(10), NoSpans, recent);

        // assert
        tip.Should().BeNull("5 minutes have not passed");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TipsShouldRequireBothToggles(bool coaching, bool liveTips)
        => Evaluate(Entry(60, 20), Fillers(9), Fillers(10), NoSpans, NoTip,
                new UserCoachSettings { IsCoachingEnabled = coaching, AreLiveTipsEnabled = liveTips })
            .Should().BeNull();

    [Fact]
    public void TipsShouldOnlyFireForTodaysEntries()
    {
        // arrange: a re-tagged entry from yesterday crossing a step on yesterday's day
        var yesterday = Entry(60, 20) with { OccurredAt = Now - TimeSpan.FromDays(1) };
        var before = Fillers(9) with { Day = Day - TimeSpan.FromDays(1) };
        var after = Fillers(10) with { Day = Day - TimeSpan.FromDays(1) };

        // act
        var tip = Evaluate(yesterday, before, after, NoSpans, NoTip);

        // assert
        tip.Should().BeNull("a tip is live feedback on what was just said");
    }

    [Fact]
    public void PaceTipShouldCarryTheRecommendedRange()
    {
        // act
        var tip = Evaluate(Entry(60, 20), new CoachDay(Day), new CoachDay(Day), NoSpans, NoTip);

        // assert
        tip!.Kind.Should().Be(CoachTipKind.SlowDown);
        tip.PaceSlowWpm.Should().Be((int)S.PaceSlowWpm, "the card shows the good band, not the tip threshold");
        tip.PaceFastWpm.Should().Be((int)S.PaceFastWpm);
    }
}
