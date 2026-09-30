using ActualChat.Chat.Coach;

namespace ActualChat.Chat.UnitTests.Coach;

public class ConversationStatsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ChatId ChatId = GroupChatId.New();
    private static readonly AuthorId Me = AuthorId.New(ChatId, 1);
    private static readonly AuthorId Other = AuthorId.New(ChatId, 2);

    private static ChatEntry Voice(long lid, AuthorId author, double from, double to)
        => new TextEntry(ChatEntryId.New(ChatId, lid)) {
            AuthorId = author,
            BeginsAt = T0 + TimeSpan.FromSeconds(from),
            EndsAt = T0 + TimeSpan.FromSeconds(to),
            Audio = new ChatEntryAudio { MediaId = MediaId.Parse("fake:m") },
        };

    [Fact]
    public void ComputeShouldGiveShareTurnsAndMonologue()
    {
        // arrange: me 0-10 and 10-15 (one 15 s turn), other 16-20, me 21-24
        var entries = new[] {
            Voice(1, Me, 0, 10), Voice(2, Me, 10, 15), Voice(3, Other, 16, 20), Voice(4, Me, 21, 24),
        };

        // act
        var s = ConversationStats.Compute(entries, Me, maxResponseGapSeconds: 5)!;

        // assert
        s.OwnSpeechSeconds.Should().Be(18);
        s.TotalSpeechSeconds.Should().Be(22);
        s.OwnTurns.Should().Be(2);
        s.TotalTurns.Should().Be(3);
        s.Participants.Should().Be(2);
        s.LongestMonologueSeconds.Should().Be(15);
    }

    [Fact]
    public void ComputeShouldKeepAForceSplitMonologueWhole()
    {
        // arrange: one 8 min talk cut into entries of at most 3 min, boundaries half a second apart
        var entries = new[] { Voice(1, Me, 0, 180), Voice(2, Me, 180.5, 360.5), Voice(3, Me, 361, 480) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5, monologueJoinGapSeconds: 5)!;

        // assert
        s.LongestMonologueSeconds.Should().Be(480);
        s.OwnTurns.Should().Be(1, "the split does not create turns");
    }

    [Fact]
    public void ComputeShouldNotJoinMessagesRecordedFarApart()
    {
        // arrange: two 1 min voice messages ten minutes apart, nobody replied in between
        var entries = new[] { Voice(1, Me, 0, 60), Voice(2, Me, 660, 720) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5, monologueJoinGapSeconds: 5)!;

        // assert
        s.LongestMonologueSeconds.Should().Be(60, "the silence between two messages is not a monologue");
        s.OwnTurns.Should().Be(1);
    }

    [Theory]
    [InlineData(5, 65)]
    [InlineData(5.5, 30)]
    public void ComputeShouldJoinOnlyWithinTheGap(double gap, double expectedMonologue)
    {
        // arrange
        var entries = new[] { Voice(1, Me, 0, 30), Voice(2, Me, 30 + gap, 60 + gap) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5, monologueJoinGapSeconds: 5)!;

        // assert
        s.LongestMonologueSeconds.Should().Be(expectedMonologue);
    }

    [Fact]
    public void ComputeShouldMeasurePatienceAsGapAfterTheOtherStops()
    {
        // arrange: other ends at 20, me starts at 21 (gap 1); the 9 s gap later is above the cap
        var entries = new[] {
            Voice(1, Other, 16, 20), Voice(2, Me, 21, 24), Voice(3, Other, 30, 31), Voice(4, Me, 40, 41),
        };

        // act
        var s = ConversationStats.Compute(entries, Me, 5)!;

        // assert
        s.Responses.Should().Be(1, "a gap above the cap is a new topic, not a response");
        s.ResponseGapSeconds.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldCountInterruptions()
    {
        // arrange: other 0-10, me starting at 8
        var entries = new[] { Voice(1, Other, 0, 10), Voice(2, Me, 8, 12) };

        // act
        var s = ConversationStats.Compute(entries, Me, 5)!;

        // assert
        s.Interruptions.Should().Be(1);
        s.Responses.Should().Be(0);
    }

    [Fact]
    public void ComputeShouldIgnoreTextOnlyAndRemovedEntries()
    {
        // arrange
        var text = new TextEntry(ChatEntryId.New(ChatId, 2)) { AuthorId = Other, BeginsAt = T0 };
        var removed = Voice(3, Other, 5, 9) with { IsRemoved = true };

        // act
        var s = ConversationStats.Compute([Voice(1, Me, 0, 4), text, removed], Me, 5)!;

        // assert
        s.TotalSpeechSeconds.Should().Be(4);
        s.Participants.Should().Be(1);
    }

    [Fact]
    public void ComputeShouldReturnNullWhenAuthorHasNoVoice()
        => ConversationStats.Compute([Voice(1, Other, 0, 4)], Me, 5).Should().BeNull();
}
