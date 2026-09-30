using ActualChat.Chat;
using ActualChat.Users.Module;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachTipPolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CoachScoringSettings S = new();
    private static readonly UserCoachSettings OptedIn = new() { IsCoachingEnabled = true, AreLiveTipsEnabled = true };
    private static readonly UserCoachTip NoTip = new();
    private static readonly ApiArray<SpeechSpan> NoSpans = ApiArray<SpeechSpan>.Empty;

    private static CoachRecord Entry(
        int words, double speechSeconds, Moment? at = null, params (SpeechSpanKind Kind, string Word)[] spans)
    {
        var occurredAt = at ?? Now;
        var apiSpans = spans
            .Select(s => new SpeechSpan(s.Kind, s.Word, 0, s.Word.Length, ApiArray<string>.Empty))
            .ToApiArray();
        var fillers = spans.Count(s => s.Kind is SpeechSpanKind.Filler or SpeechSpanKind.FilledPause);
        var weak = spans.Count(s => s.Kind == SpeechSpanKind.Weak);
        return new (
            CoachRecordKind.Entry,
            $"e{occurredAt.EpochOffset.Ticks}",
            UserId.New(),
            GroupChatId.New(),
            occurredAt
        ) {
            Entry = new CoachEntryRecord(1, "en-US", speechSeconds, speechSeconds, words, 2, 0, 0, words, 0, 0, true,
                0, fillers, weak, 0, apiSpans),
        };
    }

    private static (SpeechSpanKind, string) Filler(string word) => (SpeechSpanKind.Filler, word);

    private static UserCoachTip? Evaluate(
        CoachRecord record, IReadOnlyList<CoachRecord> window, ApiArray<SpeechSpan> spans, UserCoachTip previous,
        UserCoachSettings? settings = null)
        => CoachTipPolicy.Evaluate(record, window, spans, previous, settings ?? OptedIn, S, Now, "en-US");

    [Fact]
    public void ThreeUsesOfAWordInTheWindowShouldTip()
    {
        // arrange
        var earlier = Entry(20, 10, Now - TimeSpan.FromMinutes(15), Filler("you know"), Filler("you know"));
        var current = Entry(20, 10, Now, Filler("you know"));

        // act
        var tip = Evaluate(current, [earlier, current], NoSpans, NoTip);

        // assert
        tip!.Kind.Should().Be(CoachTipKind.Filler);
        tip.Word.Should().Be("you know");
        tip.Count.Should().Be(3, "the count is what the window holds, not the day");
        tip.WindowMinutes.Should().Be((int)S.TipWindow.TotalMinutes);
        tip.LastTipAt.Should().Be(Now);
        tip.WordTipAt["you know"].Should().Be(Now);
    }

    [Fact]
    public void UsesOutsideTheWindowShouldNotCount()
    {
        // arrange: nine uses over the day, none dense enough
        var old = Entry(20, 10, Now - TimeSpan.FromHours(3),
            Filler("you know"), Filler("you know"), Filler("you know"), Filler("you know"));
        var current = Entry(20, 10, Now, Filler("you know"), Filler("you know"));

        // act
        var tip = Evaluate(current, [current], NoSpans, NoTip);

        // assert
        tip.Should().BeNull("only two uses fall inside the window");
        old.Should().NotBeNull();
    }

    [Fact]
    public void TheSameWordShouldNotTipAgainInsideItsCooldown()
    {
        // arrange
        var previous = new UserCoachTip {
            Kind = CoachTipKind.Filler, Word = "you know", IsDismissed = true,
            LastTipAt = Now - TimeSpan.FromMinutes(30),
            WordTipAt = new ApiMap<string, Moment>(new Dictionary<string, Moment> {
                ["you know"] = Now - TimeSpan.FromMinutes(30),
            }),
        };
        var current = Entry(20, 10, Now, Filler("you know"), Filler("you know"), Filler("you know"));

        // act
        var tip = Evaluate(current, [current], NoSpans, previous);

        // assert
        tip.Should().BeNull("the word was tipped 30 minutes ago, inside its 60-minute cooldown");
    }

    [Fact]
    public void AnotherWordShouldTipDuringTheFirstWordsCooldown()
    {
        // arrange
        var previous = new UserCoachTip {
            Kind = CoachTipKind.Filler, Word = "you know", IsDismissed = true,
            LastTipAt = Now - TimeSpan.FromMinutes(30),
            WordTipAt = new ApiMap<string, Moment>(new Dictionary<string, Moment> {
                ["you know"] = Now - TimeSpan.FromMinutes(30),
            }),
        };
        var current = Entry(20, 10, Now, Filler("you know"), Filler("you know"), Filler("you know"),
            Filler("like"), Filler("like"), Filler("like"));

        // act
        var tip = Evaluate(current, [current], NoSpans, previous);

        // assert
        tip!.Word.Should().Be("like");
        tip.WordTipAt.Keys.Should().BeEquivalentTo(["you know", "like"], "the map remembers every tipped word");
    }

    [Fact]
    public void WeakWordShouldTipOnlyWithSynonyms()
    {
        // arrange
        var synonyms = ApiArray.New("excellent", "superb");
        var spans = ApiArray.New(new SpeechSpan(SpeechSpanKind.Weak, "awesome", 0, 7, synonyms));
        var weak = (SpeechSpanKind.Weak, "awesome");
        var current = Entry(20, 10, Now, weak, weak, weak);

        // act
        var with = Evaluate(current, [current], spans, NoTip);
        var without = Evaluate(current, [current], NoSpans, NoTip);

        // assert
        with!.Kind.Should().Be(CoachTipKind.WeakWord);
        with.Count.Should().Be(3);
        with.Synonyms.Should().Equal("excellent", "superb");
        without.Should().BeNull();
    }

    [Fact]
    public void PaceShouldBeJudgedOverTheWindow()
    {
        // arrange: three short entries, none reaching the word floor alone, all too fast together
        var a = Entry(12, 3, Now - TimeSpan.FromMinutes(10));
        var b = Entry(12, 3, Now - TimeSpan.FromMinutes(5));
        var c = Entry(12, 3, Now);

        // act
        var alone = Evaluate(c, [c], NoSpans, NoTip);
        var together = Evaluate(c, [a, b, c], NoSpans, NoTip);

        // assert
        alone.Should().BeNull("12 words are below the floor");
        together!.Kind.Should().Be(CoachTipKind.SlowDown);
        together.Wpm.Should().Be(240);
        together.PaceSlowWpm.Should().Be(130, "the English band");
        together.PaceFastWpm.Should().Be(170);
    }

    [Fact]
    public void WordTipShouldWinOverPace()
    {
        // arrange
        var current = Entry(60, 10, Now, Filler("like"), Filler("like"), Filler("like"));

        // act
        var tip = Evaluate(current, [current], NoSpans, NoTip);

        // assert
        tip!.Kind.Should().Be(CoachTipKind.Filler);
    }

    [Fact]
    public void TipsShouldRespectTheInterval()
    {
        // arrange
        var recent = new UserCoachTip {
            Kind = CoachTipKind.SpeedUp, LastTipAt = Now - TimeSpan.FromMinutes(2), IsDismissed = true,
        };
        var current = Entry(60, 10, Now, Filler("like"), Filler("like"), Filler("like"));

        // act
        var tip = Evaluate(current, [current], NoSpans, recent);

        // assert
        tip.Should().BeNull("5 minutes have not passed");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TipsShouldRequireBothToggles(bool coaching, bool liveTips)
    {
        // arrange
        var current = Entry(60, 10, Now, Filler("like"), Filler("like"), Filler("like"));

        // act
        var tip = Evaluate(current, [current], NoSpans, NoTip,
            new UserCoachSettings { IsCoachingEnabled = coaching, AreLiveTipsEnabled = liveTips });

        // assert
        tip.Should().BeNull();
    }

    [Fact]
    public void AReTaggedOldEntryShouldNotTip()
    {
        // arrange: the entry itself is older than the window
        var old = Entry(60, 10, Now - TimeSpan.FromHours(2), Filler("like"), Filler("like"), Filler("like"));

        // act
        var tip = Evaluate(old, [old], NoSpans, NoTip);

        // assert
        tip.Should().BeNull("a tip is live feedback on what was just said");
    }

    private static CoachRecord EntryIn(string language, int words, double seconds, Moment at,
        params (SpeechSpanKind Kind, string Word)[] spans)
    {
        var r = Entry(words, seconds, at, spans);
        return r with { Entry = r.Entry! with { Language = language } };
    }

    [Fact]
    public void AWindowWithNoFillersShouldEarnOneCleanTipPerDay()
    {
        // arrange
        var earlier = Entry(120, 60, Now - TimeSpan.FromMinutes(10));
        var current = Entry(60, 30, Now);

        // act
        var tip = Evaluate(current, [earlier, current], NoSpans, NoTip);
        var again = Evaluate(
            current, [earlier, current], NoSpans, tip! with { LastTipAt = Now - TimeSpan.FromHours(1) });

        // assert
        tip!.Kind.Should().Be(CoachTipKind.Clean);
        tip.CleanTipDay.Should().Be(UsageDay.DayOf(Now));
        again.Should().BeNull("one clean tip a day");
    }

    [Fact]
    public void AWeakWordInTheWindowShouldBlockTheCleanTip()
    {
        var earlier = Entry(120, 60, Now - TimeSpan.FromMinutes(10), (SpeechSpanKind.Weak, "very"));
        var current = Entry(60, 30, Now);
        Evaluate(current, [earlier, current], NoSpans, NoTip).Should().BeNull();
    }

    [Fact]
    public void OtherLanguagesShouldNotCountTowardAWordTip()
    {
        // arrange
        var russian = EntryIn("ru-RU", 20, 10, Now - TimeSpan.FromMinutes(5), Filler("like"), Filler("like"));
        var current = Entry(20, 10, Now, Filler("like"));

        // act
        var tip = Evaluate(current, [russian, current], NoSpans, NoTip);

        // assert
        tip.Should().BeNull("two of the three uses are in another language");
    }

    [Fact]
    public void ZeroIntervalShouldMeanOneTipPerConversation()
    {
        // arrange
        var perConversation = OptedIn with { TipInterval = TimeSpan.Zero };
        var a = Entry(20, 10, Now - TimeSpan.FromMinutes(5), Filler("like"), Filler("like"));
        var current = Entry(20, 10, Now, Filler("like"));
        var earlierTip = new UserCoachTip {
            Kind = CoachTipKind.Filler, ChatId = current.ChatId, LastTipAt = Now - TimeSpan.FromMinutes(6),
        };

        // act
        var sameChat = Evaluate(current, [a, current], NoSpans, earlierTip, perConversation);
        var otherChat = Evaluate(
            current, [a, current], NoSpans, earlierTip with { ChatId = GroupChatId.New() }, perConversation);

        // assert
        sameChat.Should().BeNull("a tip already fired in this conversation");
        otherChat.Should().NotBeNull("another chat is another conversation");
    }
}
