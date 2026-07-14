using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests;

public class ContextStartScannerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly DateTime BaseTime = new (2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly AuthorId _authorId = AuthorId.New(GroupChatId.New(), 1);

    private ChatEntrySlim Entry(long lid, double seconds, bool isTranscript = false)
        => new (lid, "x", _authorId, BaseTime.AddSeconds(seconds), null, isTranscript, null, false);

    [Fact]
    public void EmptyPrecedingReturnsAnchor()
    {
        // arrange
        var anchor = Entry(100, 500);

        // act
        var result = ContextStartScanner.FindContextStartLid([], anchor);

        // assert
        result.Should().Be(100);
    }

    [Fact]
    public void TextGapBelowFiveMinutesIsIncluded()
    {
        // arrange
        var preceding = new List<ChatEntrySlim> { Entry(1, 0) };
        var anchor = Entry(2, 200); // 200s < 300s (5 min) text boundary

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(1);
    }

    [Fact]
    public void TextGapAtFiveMinutesIsBoundary()
    {
        // arrange
        var preceding = new List<ChatEntrySlim> { Entry(1, 0) };
        var anchor = Entry(2, 300); // exactly 300s (5 min) - not < minPause, so a boundary

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(2);
    }

    [Fact]
    public void TranscriptGapBelowMaxStreamDurationIsIncluded()
    {
        // arrange
        var preceding = new List<ChatEntrySlim> { Entry(1, 0, isTranscript: true) };
        var anchor = Entry(2, 170, isTranscript: true); // 170s < 180s (MaxStreamDuration)

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(1);
    }

    [Fact]
    public void TranscriptGapAtMaxStreamDurationIsBoundary()
    {
        // arrange
        var preceding = new List<ChatEntrySlim> { Entry(1, 0, isTranscript: true) };
        var anchor = Entry(2, 180, isTranscript: true); // exactly 180s - boundary

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(2);
    }

    [Fact]
    public void LargeGapWithinTenTimesAverageIsIncluded()
    {
        // arrange - small established pauses raise the running average so an 800s gap (> 300s minPause)
        // still falls under 10x the average and is pulled into the context.
        var preceding = new List<ChatEntrySlim> {
            Entry(1, 0),
            Entry(2, 800),
            Entry(3, 900),
        };
        var anchor = Entry(4, 1000);

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(1);
    }

    [Fact]
    public void GapAtTwelveHourCapIsBoundaryEvenUnderAverage()
    {
        // arrange - the running average grows large enough that 10x average would admit a 13h gap, but the
        // 12h hard cap blocks it. Backward pauses from anchor: 250, 2000, 10000, 40000, then 46800 (13h).
        var preceding = new List<ChatEntrySlim> {
            Entry(0, 0),        // gap to e1 = 46800 (13h) - the cap boundary
            Entry(1, 46800),
            Entry(2, 86800),
            Entry(3, 96800),
            Entry(4, 98800),
        };
        var anchor = Entry(5, 99050);

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(1);
    }

    [Fact]
    public void GapBelowTwelveHourCapUnderAverageIsIncluded()
    {
        // arrange - same shape as above but the final gap is 43000s (< 12h) and under 10x average, so it is
        // pulled in, proving the previous case's boundary was the cap and not the average rule.
        var preceding = new List<ChatEntrySlim> {
            Entry(0, 55800),    // gap to e1 = 43000 (< 12h)
            Entry(1, 98800),
            Entry(2, 138800),
            Entry(3, 148800),
            Entry(4, 150800),
        };
        var anchor = Entry(5, 151050);

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(0);
    }

    [Fact]
    public void AllWithinGroupReturnsEarliest()
    {
        // arrange - a long run of sub-minPause text pauses is scanned end to end.
        var preceding = Enumerable.Range(0, 50)
            .Select(i => Entry(i, i * 100.0))
            .ToList();
        var anchor = Entry(50, 50 * 100.0);

        // act
        var result = ContextStartScanner.FindContextStartLid(preceding, anchor);

        // assert
        result.Should().Be(0);
    }
}
