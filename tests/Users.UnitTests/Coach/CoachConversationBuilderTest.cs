using ActualChat.Chat;

namespace ActualChat.Users.UnitTests.Coach;

public class CoachConversationBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment T0 = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(30);
    private static readonly ChatId ChatA = GroupChatId.New();

    private static CoachRecord Entry(ChatId chatId, long lid, Moment at, int words, string language = "en-US",
        int fillers = 0, double seconds = 60)
        => new (CoachRecordKind.Entry, $"{chatId}:{lid}", UserId.New(), chatId, at) {
            Entry = new CoachEntryRecord(lid, language, seconds, seconds, words, 3, 0, 0, words, 0, 0, true, 0, fillers, 0, 0,
                Enumerable.Range(0, fillers)
                    .Select(i => new SpeechSpan(SpeechSpanKind.Filler, "like", i * 5, 4, ApiArray<string>.Empty))
                    .ToApiArray()),
        };

    private static CoachRecord Run(ChatId chatId, long startLid, Moment at, double own, double total, double monologue)
        => new (CoachRecordKind.Run, $"{chatId}:run:{startLid}", UserId.New(), chatId, at) {
            Run = new CoachRunRecord(startLid, own, total, 2, 5, 3, monologue, 1, 0.9, 0),
        };

    [Fact]
    public void EntriesWithinTheGapShouldFormOneConversationNewestFirst()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 1, T0, 100),
            Entry(ChatA, 2, T0 + TimeSpan.FromMinutes(10), 50, fillers: 3),
            Entry(ChatA, 3, T0 + TimeSpan.FromMinutes(42), 30),
            Run(ChatA, 1, T0 + TimeSpan.FromMinutes(12), 30, 90, 20),
        };

        // act
        var conversations = CoachConversationBuilder.Build(records, Gap);

        // assert
        conversations.Should().HaveCount(2, "31 minutes of silence ends a conversation");
        conversations[0].StartEntryLid.Should().Be(3);
        var first = conversations[1];
        first.Words.Should().Be(150);
        first.Fillers.Should().Be(3);
        first.FillerCounts["like"].Should().Be(3);
        first.TalkShare.Should().BeApproximately(30d / 90, 1e-9);
        first.LongestMonologueSeconds.Should().Be(20);
        first.Pace.Should().BeApproximately(150 * 60 / 120d, 1e-9);
    }

    [Fact]
    public void AMixedLanguageRunShouldSplitIntoOneConversationPerLanguage()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 1, T0, 100, "ru-RU", fillers: 4),
            Entry(ChatA, 2, T0 + TimeSpan.FromMinutes(1), 60, "en-US", fillers: 1),
            Entry(ChatA, 3, T0 + TimeSpan.FromMinutes(2), 40, "ru-RU"),
            Run(ChatA, 1, T0 + TimeSpan.FromMinutes(3), 30, 90, 20),
        };

        // act
        var conversations = CoachConversationBuilder.Build(records, Gap);

        // assert
        conversations.Should().HaveCount(2, "one run, two languages");
        var en = conversations.Single(c => c.Language == "en");
        var ru = conversations.Single(c => c.Language == "ru");
        en.Words.Should().Be(60);
        en.Fillers.Should().Be(1);
        ru.Words.Should().Be(140);
        ru.Fillers.Should().Be(4);
        ru.Pace.Should().BeApproximately(140 * 60 / 120d, 1e-9, "only its own entries set the pace");
        en.TalkShare.Should().Be(ru.TalkShare, "turn-taking belongs to the whole run");
        en.SecondaryLanguage.Should().BeNull();
        conversations[0].StartedAt.Should().BeGreaterThan(conversations[1].StartedAt, "newest first");
    }

    [Fact]
    public void OneLongEntryShouldStayOneConversation()
    {
        // arrange
        var record = Entry(ChatA, 1, T0, 900, seconds: 2700);

        // act
        var conversations = CoachConversationBuilder.Build([record], Gap);

        // assert
        conversations.Should().ContainSingle().Which.EndedAt.Should().Be(T0 + TimeSpan.FromSeconds(2700));
    }

    [Fact]
    public void ChatsShouldNeverMix()
    {
        // arrange
        var chatB = GroupChatId.New();

        // act
        var conversations = CoachConversationBuilder.Build(
            [Entry(ChatA, 1, T0, 10), Entry(chatB, 1, T0 + TimeSpan.FromMinutes(1), 10)], Gap);

        // assert
        conversations.Should().HaveCount(2);
    }

    [Fact]
    public void ACutLogShouldNotShowItsOldestConversationBecauseItMayBeMissingItsStart()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 5, T0 + TimeSpan.FromHours(3), 30),
            Entry(ChatA, 4, T0 + TimeSpan.FromHours(1), 30),
        };

        // act
        var whole = CoachConversationBuilder.Build(records, Gap);
        var cut = CoachConversationBuilder.Build(records, Gap, isLogCut: true);
        var onlyOne = CoachConversationBuilder.Build([records[0]], Gap, isLogCut: true);

        // assert
        whole.Select(c => c.StartEntryLid).Should().Equal(5, 4);
        cut.Select(c => c.StartEntryLid).Should().Equal(5);
        onlyOne.Should().ContainSingle("a lone conversation stays, however incomplete");
    }

    [Fact]
    public void ACutLogShouldDropEveryLanguageOfItsOldestRun()
    {
        // arrange
        var records = new[] {
            Entry(ChatA, 5, T0 + TimeSpan.FromHours(3), 30),
            Entry(ChatA, 4, T0 + TimeSpan.FromHours(1), 30, "ru-RU"),
            Entry(ChatA, 3, T0 + TimeSpan.FromHours(1), 30, "en-US"),
        };

        // act
        var cut = CoachConversationBuilder.Build(records, Gap, isLogCut: true);

        // assert
        cut.Select(c => c.StartEntryLid).Should().Equal(5);
    }
}
