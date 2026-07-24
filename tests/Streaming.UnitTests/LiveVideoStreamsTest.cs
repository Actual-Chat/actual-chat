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

        var result = LiveVideoStreams.GetUpgradedStreams(previous, current).ToArray();

        result.Should().ContainSingle(x => x.StreamId == "screen" && x.WasAbsent);
    }

    [Fact]
    public void GetUpgradedStreams_TreatsFirstExplicitEnvelopeAsUpgrade()
    {
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(null, current).ToArray();

        result.Should().ContainSingle(x => x.StreamId == "screen" && x.WasAbsent);
    }

    [Fact]
    public void GetUpgradedStreams_MarksReAddedStreamAsWasAbsent()
    {
        // arrange
        var previous = new ApiMap<string, ReceiveQuality>();
        var current = new ApiMap<string, ReceiveQuality> { ["s1"] = new ReceiveQuality(1) };

        // act
        var result = LiveVideoStreams.GetUpgradedStreams(previous, current).ToArray();

        // assert
        result.Should().ContainSingle(x => x.StreamId == "s1" && x.WasAbsent);
    }

    [Fact]
    public void GetUpgradedStreams_MarksGenuineUpgradeAsPresent()
    {
        // arrange
        var previous = new ApiMap<string, ReceiveQuality> { ["s1"] = new ReceiveQuality(0) };
        var current = new ApiMap<string, ReceiveQuality> { ["s1"] = new ReceiveQuality(2) };

        // act
        var result = LiveVideoStreams.GetUpgradedStreams(previous, current).ToArray();

        // assert
        result.Should().ContainSingle(x => x.StreamId == "s1" && !x.WasAbsent);
    }

    [Fact]
    public void ComputeDemandSnapshot_ZeroViewersIsEmpty()
    {
        // act
        var snapshot = VideoStreamingBackend.ComputeDemandSnapshot([]);

        // assert
        snapshot.Should().Be(VideoStreamingBackend.DemandSnapshot.None);
    }

    [Fact]
    public void ComputeDemandSnapshot_SingleThumbnailViewerIsThumbnailOnly()
    {
        // act
        var snapshot = VideoStreamingBackend.ComputeDemandSnapshot(
            [new ReceiveQuality(0, isThumbnail: true)]);

        // assert
        snapshot.Should().Be(new VideoStreamingBackend.DemandSnapshot(1, true, 1, 0));
    }

    [Fact]
    public void ComputeDemandSnapshot_AnyLargeViewerIsNotThumbnailOnly()
    {
        // act
        var snapshot = VideoStreamingBackend.ComputeDemandSnapshot([
            new ReceiveQuality(0, isThumbnail: true),
            new ReceiveQuality(2),
        ]);

        // assert
        snapshot.Should().Be(new VideoStreamingBackend.DemandSnapshot(0b101, false, 2, 0));
    }

    [Fact]
    public void ComputeDemandSnapshot_PausedViewersAreIgnored()
    {
        // act + assert
        VideoStreamingBackend.ComputeDemandSnapshot([
                new ReceiveQuality(0, isThumbnail: true),
                ReceiveQuality.Paused,
            ])
            .Should().Be(new VideoStreamingBackend.DemandSnapshot(1, true, 2, 1));
        VideoStreamingBackend.ComputeDemandSnapshot([ReceiveQuality.Paused])
            .Should().Be(new VideoStreamingBackend.DemandSnapshot(0, false, 1, 1));
    }
}
