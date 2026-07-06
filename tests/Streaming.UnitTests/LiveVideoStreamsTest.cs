using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

public class LiveVideoStreamsTest
{
    [Fact]
    public void GetUpgradedStreams_TreatsMissingPreviousStreamAsLowest()
    {
        var previous = new ApiMap<string, ReceiveQuality> {
            ["camera"] = ReceiveQuality.Lowest,
        };
        var current = new ApiMap<string, ReceiveQuality> {
            ["camera"] = ReceiveQuality.Lowest,
            ["screen"] = new ReceiveQuality(1),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(previous, current);

        result.Should().Equal("screen");
    }

    [Fact]
    public void GetUpgradedStreams_TreatsFirstExplicitEnvelopeAsUpgrade()
    {
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(null, current);

        result.Should().Equal("screen");
    }

    [Fact]
    public void ComputeThumbnailViewersOnly_ZeroViewersIsFalse()
        => LiveVideoStreams.ComputeThumbnailViewersOnly([]).Should().BeFalse();

    [Fact]
    public void ComputeThumbnailViewersOnly_SingleThumbnailViewerIsTrue()
        => LiveVideoStreams.ComputeThumbnailViewersOnly([new ReceiveQuality(0, isThumbnail: true)])
            .Should().BeTrue();

    [Fact]
    public void ComputeThumbnailViewersOnly_AnyLargeViewerIsFalse()
        => LiveVideoStreams.ComputeThumbnailViewersOnly([
                new ReceiveQuality(0, isThumbnail: true),
                new ReceiveQuality(2),
            ])
            .Should().BeFalse();

    [Fact]
    public void ComputeThumbnailViewersOnly_PausedViewersAreIgnored()
    {
        LiveVideoStreams.ComputeThumbnailViewersOnly([
                new ReceiveQuality(0, isThumbnail: true),
                ReceiveQuality.Paused,
            ])
            .Should().BeTrue();
        LiveVideoStreams.ComputeThumbnailViewersOnly([ReceiveQuality.Paused])
            .Should().BeFalse();
    }
}
