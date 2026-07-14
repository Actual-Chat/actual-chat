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
    public void ComputeDemandSnapshot_ZeroViewersIsEmpty()
    {
        // act
        var (mask, thumbnailOnly) = VideoStreamingBackend.ComputeDemandSnapshot([]);

        // assert
        mask.Should().Be(0);
        thumbnailOnly.Should().BeFalse();
    }

    [Fact]
    public void ComputeDemandSnapshot_SingleThumbnailViewerIsThumbnailOnly()
    {
        // act
        var (mask, thumbnailOnly) = VideoStreamingBackend.ComputeDemandSnapshot(
            [new ReceiveQuality(0, isThumbnail: true)]);

        // assert
        mask.Should().Be(1);
        thumbnailOnly.Should().BeTrue();
    }

    [Fact]
    public void ComputeDemandSnapshot_AnyLargeViewerIsNotThumbnailOnly()
    {
        // act
        var (mask, thumbnailOnly) = VideoStreamingBackend.ComputeDemandSnapshot([
            new ReceiveQuality(0, isThumbnail: true),
            new ReceiveQuality(2),
        ]);

        // assert
        mask.Should().Be(0b101);
        thumbnailOnly.Should().BeFalse();
    }

    [Fact]
    public void ComputeDemandSnapshot_PausedViewersAreIgnored()
    {
        // act + assert
        VideoStreamingBackend.ComputeDemandSnapshot([
                new ReceiveQuality(0, isThumbnail: true),
                ReceiveQuality.Paused,
            ])
            .Should().Be((1, true));
        VideoStreamingBackend.ComputeDemandSnapshot([ReceiveQuality.Paused])
            .Should().Be((0, false));
    }
}
