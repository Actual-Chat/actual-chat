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

        result.Should().Equal(("screen", true));
    }

    [Fact]
    public void GetUpgradedStreams_TreatsFirstExplicitEnvelopeAsUpgrade()
    {
        var current = new ApiMap<string, ReceiveQuality> {
            ["screen"] = new ReceiveQuality(1),
        };

        var result = LiveVideoStreams.GetUpgradedStreams(null, current).ToArray();

        result.Should().Equal(("screen", true));
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
        result.Should().Equal(("s1", true));
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
        result.Should().Equal(("s1", false));
    }

    [Fact]
    public void ShouldSendReAddPli_NoPriorStampAllowsPli()
    {
        // arrange
        var now = Moment.Now;

        // act
        var result = LiveVideoStreams.ShouldSendReAddPli(null, now, TimeSpan.FromSeconds(30));

        // assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldSendReAddPli_PriorStampWithinCooldownSuppressesPli()
    {
        // arrange
        var now = Moment.Now;
        var lastPliAt = now - TimeSpan.FromSeconds(10);

        // act
        var result = LiveVideoStreams.ShouldSendReAddPli(lastPliAt, now, TimeSpan.FromSeconds(30));

        // assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldSendReAddPli_PriorStampAtOrBeyondCooldownAllowsPli()
    {
        // arrange
        var now = Moment.Now;
        var cooldown = TimeSpan.FromSeconds(30);

        // act + assert
        LiveVideoStreams.ShouldSendReAddPli(now - cooldown, now, cooldown).Should().BeTrue();
        LiveVideoStreams.ShouldSendReAddPli(now - cooldown - TimeSpan.FromSeconds(1), now, cooldown).Should().BeTrue();
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

    [Fact]
    public void ClientStatsRejectsNonFiniteValues()
    {
        // act + assert
        ClientStats.Ratio(double.NaN).Should().BeNull();
        ClientStats.Ratio(double.PositiveInfinity).Should().BeNull();
        ClientStats.AckAgeMs(double.NaN).Should().BeNull();
        ClientStats.AckAgeMs(double.NegativeInfinity).Should().BeNull();
    }

    [Fact]
    public void ClientStatsClampsOutOfRangeValues()
    {
        // act + assert
        ClientStats.Ratio(-5).Should().Be(0);
        ClientStats.Ratio(17).Should().Be(1);
        ClientStats.Ratio(0.25).Should().Be(0.25);
        ClientStats.AckAgeMs(-1000).Should().Be(-1);
        ClientStats.AckAgeMs(double.MaxValue).Should().Be(ClientStats.MaxAckAgeMs);
        ClientStats.LayerCount(-3).Should().Be(0);
        ClientStats.LayerCount(1000).Should().Be(ClientStats.MaxLayerCount);
        ClientStats.ByteRate(-1).Should().Be(0);
        ClientStats.ByteRate(long.MaxValue).Should().Be(ClientStats.MaxByteRate);
        ClientStats.AudioLatency(TimeSpan.FromSeconds(-10)).Should().Be(TimeSpan.Zero);
        ClientStats.AudioLatency(TimeSpan.MaxValue).Should().Be(ClientStats.MaxAudioLatency);
        ClientStats.Quality(new ReceiveQuality(9999, isThumbnail: true))
            .Should().Be(new ReceiveQuality(ClientStats.MaxLayerId, isThumbnail: true));
        ClientStats.Quality(ReceiveQuality.Paused).Should().Be(ReceiveQuality.Paused);
    }
}
