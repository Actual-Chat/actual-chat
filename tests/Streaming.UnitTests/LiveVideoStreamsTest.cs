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
            ["screen"] = new ReceiveQuality(1, 0),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(previous, current);

        result.Should().Equal("screen");
    }

    [Fact]
    public void GetUpgradedStreams_TreatsFirstExplicitEnvelopeAsUpgrade()
    {
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1, 0),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(null, current);

        result.Should().Equal("screen");
    }

    [Fact]
    public void GetUpgradedStreams_TreatsLowerTemporalRequestAsUpgrade()
    {
        var previous = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1, 1),
        };
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1, 0),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(previous, current);

        result.Should().Equal("screen");
    }

    [Fact]
    public void GetUpgradedStreams_TreatsHigherTemporalRequestAsDowngrade()
    {
        var previous = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1, 0),
        };
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1, 1),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(previous, current);

        result.Should().BeEmpty();
    }
}
