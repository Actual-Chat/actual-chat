using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class ReplayTimelineTest
{
    [Fact]
    public void AnEntryNeverStartsBeforeThePreviousDubEnds()
    {
        // act & assert
        ReplayTimeline.PlaysAt(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12))
            .Should().Be(TimeSpan.FromSeconds(12));
        ReplayTimeline.PlaysAt(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8)).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AnUndubbedReplayNeverStretchesTheTimeline()
    {
        // act & assert
        ReplayTimeline.PlaysAt(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), stretchTimeline: false)
            .Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ASeekIntoADubbedEntryIsScaledToTheDubsLength()
    {
        // act & assert
        ReplayTimeline.ScaleSkip(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(9))
            .Should().Be(TimeSpan.FromSeconds(4.5));
        ReplayTimeline.ScaleSkip(TimeSpan.FromSeconds(3), TimeSpan.Zero, TimeSpan.FromSeconds(9))
            .Should().Be(TimeSpan.Zero, "an entry without a known duration can't be scaled, so the dub starts over");
    }

    [Fact]
    public void APauseBetweenEntriesShouldBeCutDownToMaxGap()
    {
        // arrange
        var maxGap = TimeSpan.FromSeconds(0.5);

        // act & assert
        ReplayTimeline.SkippedGap(TimeSpan.FromSeconds(3), maxGap).Should().Be(TimeSpan.FromSeconds(2.5));
        ReplayTimeline.SkippedGap(TimeSpan.FromSeconds(0.3), maxGap)
            .Should().Be(TimeSpan.Zero, "a pause shorter than the max gap is kept as it is");
        ReplayTimeline.SkippedGap(TimeSpan.FromSeconds(-1), maxGap)
            .Should().Be(TimeSpan.Zero, "overlapping entries have no pause to cut");
    }

    [Fact]
    public void TailCutoffShouldBeCountedFromTheSkip()
    {
        // arrange
        var entry = NewEntry(lead: TimeSpan.Zero, speech: TimeSpan.FromSeconds(10));
        var margin = TimeSpan.FromSeconds(0.4);

        // act & assert
        ReplayTimeline.TailCutoff(entry, TimeSpan.Zero, margin).Should().Be(TimeSpan.FromSeconds(10.4));
        ReplayTimeline.TailCutoff(entry, TimeSpan.FromSeconds(4), margin).Should().Be(TimeSpan.FromSeconds(6.4));
    }

    [Fact]
    public void TailCutoffShouldKeepTheSpeechOfASpeakerWhoTookTheirTime()
    {
        // arrange - the recording ran for a second before the first word
        var entry = NewEntry(lead: TimeSpan.FromSeconds(1), speech: TimeSpan.FromSeconds(10));
        var margin = TimeSpan.FromSeconds(0.4);

        // act & assert - the cut sits past the last word, not a second before it
        ReplayTimeline.TailCutoff(entry, TimeSpan.Zero, margin).Should().Be(TimeSpan.FromSeconds(11.4));
    }

    [Fact]
    public void TailCutoffShouldBeUnsetForAnEntryThatDoesntSayWhereItEnds()
    {
        // arrange
        var entry = NewEntry(lead: TimeSpan.Zero, speech: TimeSpan.FromSeconds(10)) with { EndsAt = null };

        // act & assert
        ReplayTimeline.TailCutoff(entry, TimeSpan.Zero, TimeSpan.FromSeconds(0.4)).Should().BeNull();
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.25, 0.8)]
    [InlineData(1.33, 0.75)]
    [InlineData(1.5, 2.0 / 3)]
    [InlineData(1.75, 4.0 / 7)]
    [InlineData(2.0, 0.5)]
    public void MustKeepFrameShouldKeepOneOverSpeedOfFrames(double speed, double expectedShare)
    {
        // arrange
        const int frameCount = 8400; // Divisible by 3, 4, 5, 7 and 8

        // act
        var keptCount = Enumerable.Range(0, frameCount).Count(i => ReplayTimeline.MustKeepFrame(i, speed));

        // assert
        ((double)keptCount / frameCount).Should().BeApproximately(expectedShare, 0.01);
    }

    [Fact]
    public void DeadlineShouldScaleTheFrameOffsetBySpeed()
        // act & assert
        => ReplayTimeline.Deadline(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 2.0)
            .Should().Be(TimeSpan.FromSeconds(6));

    // Private methods

    // lead is how long the recording ran before the first word - the entry begins at that word,
    // while its audio begins with the recording
    private static ChatEntry NewEntry(TimeSpan lead, TimeSpan speech)
    {
        var recordedAt = new Moment(TimeSpan.FromDays(20_000));
        return new TextEntry(ChatEntryId.Parse("the-actual-one:0:7")) {
            BeginsAt = recordedAt + lead,
            EndsAt = recordedAt + lead + speech,
            Audio = new ChatEntryAudio { BeginsAt = recordedAt },
        };
    }
}
