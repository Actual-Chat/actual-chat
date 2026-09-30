using ActualChat.Chat;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachDayBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment Day = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly UserId User = UserId.New();
    private static readonly ChatId Chat = GroupChatId.New();

    private static CoachRecord Entry(long lid, int words, int distinct, bool isTagged, params SpeechSpan[] spans)
        => new (CoachRecordKind.Entry, $"e{lid}", User, Chat, Day + TimeSpan.FromHours(lid)) {
            Entry = new CoachEntryRecord(lid, "en-US", 60, 50, words, 4, 1, 0, distinct, 2, 10, isTagged,
                spans.Count(s => s.Kind == SpeechSpanKind.FilledPause),
                spans.Count(s => s.Kind == SpeechSpanKind.Filler),
                spans.Count(s => s.Kind == SpeechSpanKind.Weak),
                0,
                spans.ToApiArray()),
        };

    private static SpeechSpan Span(SpeechSpanKind kind, string word)
        => new (kind, word, 0, word.Length, ApiArray<string>.Empty);

    private static CoachRecord Run(long start, double own, double total, int participants)
        => new (CoachRecordKind.Run, $"r{start}", User, Chat, Day + TimeSpan.FromHours(5)) {
            Run = new CoachRunRecord(start, own, total, 2, 5, participants, 40, 1, 0.8, 0),
        };

    [Fact]
    public void BuildShouldSumEntriesAndCountWords()
    {
        // arrange
        var records = new[] {
            Entry(1, 100, 80, true,
                Span(SpeechSpanKind.Filler, "you know"),
                Span(SpeechSpanKind.Filler, "you know"),
                Span(SpeechSpanKind.Weak, "awesome")),
            Entry(2, 10, 9, false),
        };

        // act
        var day = CoachDayBuilder.Build(Day, records, minVocabularyWords: 20);

        // assert
        day.Entries.Should().Be(2);
        day.TaggedEntries.Should().Be(1);
        day.Words.Should().Be(110);
        day.Fillers.Should().Be(2);
        day.WeakWords.Should().Be(1);
        day.FillerCounts["you know"].Should().Be(2);
        day.WeakWordCounts["awesome"].Should().Be(1);
        day.VocabularyWords.Should().Be(100, "entries under the vocabulary minimum do not count");
        day.VocabularyDistinct.Should().Be(80);
        day.SpeechSeconds.Should().Be(100);
    }

    [Fact]
    public void BuildShouldAggregateRunsWithFairShare()
    {
        // act
        var day = CoachDayBuilder.Build(Day, [Run(1, 30, 90, 3), Run(50, 20, 40, 2)], 20);

        // assert
        day.Runs.Should().Be(2);
        day.OwnSpeechSeconds.Should().Be(50);
        day.TotalSpeechSeconds.Should().Be(130);
        day.FairShareSeconds.Should().Be(50, "90/3 + 40/2");
        day.LongestMonologueSeconds.Should().Be(40);
        day.Responses.Should().Be(2);
        day.ResponseGapSeconds.Should().BeApproximately(1.6, 0.001);
    }

    [Fact]
    public void MergeShouldAddDaysAndWordMaps()
    {
        // arrange
        var a = CoachDayBuilder.Build(Day, [Entry(1, 100, 80, true, Span(SpeechSpanKind.Filler, "like"))], 20);
        var nextDay = Day + TimeSpan.FromDays(1);
        var later = Entry(2, 50, 40, true, Span(SpeechSpanKind.Filler, "like"))
            with { OccurredAt = nextDay + TimeSpan.FromHours(2) };
        var b = CoachDayBuilder.Build(nextDay, [later], 20);

        // act
        var merged = CoachDayBuilder.Merge(Day, [a, b]);

        // assert
        merged.Words.Should().Be(150);
        merged.FillerCounts["like"].Should().Be(2);
        merged.Entries.Should().Be(2);
    }

    [Fact]
    public void BuildShouldIgnoreRecordsOfOtherDays()
    {
        // arrange
        var other = Entry(1, 100, 80, true) with { OccurredAt = Day + TimeSpan.FromDays(2) };

        // act
        var day = CoachDayBuilder.Build(Day, [other], 20);

        // assert
        day.Entries.Should().Be(0);
    }

    private static CoachRecord EntryIn(string language, long lid, int words)
    {
        var r = Entry(lid, words, words, true);
        return r with { Entry = r.Entry! with { Language = language } };
    }

    [Fact]
    public void BuildAllShouldSplitEntriesByLanguageAndCopyRunsIntoEachRow()
    {
        // arrange
        var records = new[] {
            EntryIn("en-US", 1, 100), EntryIn("ru-RU", 2, 50), EntryIn("ru-RU", 3, 20), Run(1, 30, 90, 3),
        };

        // act
        var days = CoachDayBuilder.BuildAll(Day, records, 20);

        // assert
        days.Select(d => d.Language)
            .Should().BeEquivalentTo(["", "en", "ru"], "a runs-only neutral row joins the language rows");
        days.Single(d => d.Language == "en").Words.Should().Be(100);
        days.Single(d => d.Language == "ru").Words.Should().Be(70);
        days.Should().OnlyContain(d => d.Runs == 1 && d.OwnSpeechSeconds == 30, "runs are not language-bound");
    }

    [Fact]
    public void MergeShouldCountConversationFieldsOncePerDay()
    {
        // arrange
        var perLanguage = CoachDayBuilder.BuildAll(
            Day, [EntryIn("en-US", 1, 100), EntryIn("ru-RU", 2, 50), Run(1, 30, 90, 3)], 20);

        // act
        var merged = CoachDayBuilder.Merge(Day, perLanguage);

        // assert
        merged.Words.Should().Be(150);
        merged.Runs.Should().Be(1, "the same run sits in both language rows");
        merged.OwnSpeechSeconds.Should().Be(30);
        merged.FairShareSeconds.Should().Be(30);
    }

    [Fact]
    public void BuildAllShouldPutRunsWithoutEntriesInTheNeutralRow()
    {
        // act
        var days = CoachDayBuilder.BuildAll(Day, [Run(1, 30, 90, 3)], 20);

        // assert
        days.Should().ContainSingle().Which.Language.Should().Be("");
    }

    [Fact]
    public void ANeutralRunsOnlyRowShouldLetEveryLanguageSeeTheDaysRunsWithoutDoubleCountingWords()
    {
        // arrange
        var days = CoachDayBuilder.BuildAll(Day, [EntryIn("ru-RU", 1, 100), Run(1, 30, 90, 3)], 20);

        // act: what an English filter reads is the neutral row alone
        var forEnglish = CoachDayBuilder.Merge(Day, days.Where(d => d.Language is "en" or ""));
        var all = CoachDayBuilder.Merge(Day, days);

        // assert
        forEnglish.Words.Should().Be(0);
        forEnglish.Runs.Should().Be(1);
        forEnglish.OwnSpeechSeconds.Should().Be(30);
        all.Words.Should().Be(100);
        all.Runs.Should().Be(1, "runs count once per day however many rows carry them");
    }
}
