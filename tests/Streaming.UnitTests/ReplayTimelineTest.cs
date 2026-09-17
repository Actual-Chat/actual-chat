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
}
