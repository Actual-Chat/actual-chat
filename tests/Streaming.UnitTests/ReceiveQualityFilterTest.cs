using ActualChat.Streaming.Services;
using ActualChat.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Streaming.UnitTests;

public class ReceiveQualityFilterTest
{
    [Fact]
    public async Task LoweredCapSwitchesOnNextKeyframe()
    {
        var quality = ReceiveQuality.Default;
        var frames = Frames(
            Key(2, 1),
            Delta(2, 1),
            Mutate(() => quality = ReceiveQuality.Lowest),
            Key(2, 2),
            Key(0, 2),
            Delta(2, 2),
            Delta(0, 2));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => x.SpatialLayerId).Should().Equal((byte)2, (byte)2, (byte)0, (byte)0);
    }

    [Fact]
    public async Task LoweredCapKeepsForwardingCurrentLayerUntilKeyframe()
    {
        var quality = ReceiveQuality.Default;
        var frames = Frames(
            Key(2, 1),
            Delta(2, 1),
            Mutate(() => quality = ReceiveQuality.Lowest),
            Delta(2, 1),
            Delta(0, 1),
            Key(0, 2),
            Delta(0, 2));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => x.SpatialLayerId).Should().Equal((byte)2, (byte)2, (byte)2, (byte)0, (byte)0);
    }

    [Fact]
    public async Task UpgradedCapKeepsForwardingCurrentLayerUntilKeyframe()
    {
        var quality = ReceiveQuality.Lowest;
        var frames = Frames(
            Key(0, 1),
            Delta(0, 1),
            Mutate(() => quality = ReceiveQuality.Default),
            Delta(0, 1),
            Delta(2, 1),
            Key(2, 2),
            Delta(2, 2));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => x.SpatialLayerId).Should().Equal((byte)0, (byte)0, (byte)0, (byte)2, (byte)2);
    }

    [Fact]
    public async Task UpgradedTemporalCapWaitsForNextKeyframe()
    {
        var quality = new ReceiveQuality(2, 0);
        var frames = Frames(
            Key(2, 1),
            Delta(2, 1),
            Delta(2, 1, temporal: 1),
            Mutate(() => quality = ReceiveQuality.Default),
            Delta(2, 1, temporal: 1),
            Delta(2, 1),
            Key(2, 2),
            Delta(2, 2, temporal: 1));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => (x.KeyFrameNumber, x.TemporalLayerId)).Should().Equal(
            (1L, (byte)0),
            (1L, (byte)0),
            (1L, (byte)0),
            (2L, (byte)0),
            (2L, (byte)1));
    }

    [Fact]
    public async Task LoweredTemporalCapDoesNotReUpgradeBeforeKeyframe()
    {
        var quality = ReceiveQuality.Default;
        var frames = Frames(
            Key(2, 1),
            Delta(2, 1, temporal: 1),
            Mutate(() => quality = ReceiveQuality.Lowest),
            Delta(2, 1, temporal: 1),
            Delta(2, 1),
            Mutate(() => quality = ReceiveQuality.Default),
            Delta(2, 1, temporal: 1),
            Key(2, 2),
            Delta(2, 2, temporal: 1));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => (x.KeyFrameNumber, x.TemporalLayerId)).Should().Equal(
            (1L, (byte)0),
            (1L, (byte)1),
            (1L, (byte)0),
            (2L, (byte)0),
            (2L, (byte)1));
    }

    private static VideoFrame Key(byte spatial, long keyFrameNumber)
        => Frame(spatial, keyFrameNumber, isKeyFrame: true);

    private static VideoFrame Delta(byte spatial, long keyFrameNumber)
        => Delta(spatial, keyFrameNumber, temporal: 0);

    private static VideoFrame Delta(byte spatial, long keyFrameNumber, byte temporal)
        => Frame(spatial, keyFrameNumber, isKeyFrame: false, temporal);

    private static VideoFrame Frame(byte spatial, long keyFrameNumber, bool isKeyFrame, byte temporal = 0)
        => new(isKeyFrame) {
            Width = spatial switch {
                0 => 320,
                1 => 640,
                _ => 1280,
            },
            Height = spatial switch {
                0 => 180,
                1 => 360,
                _ => 720,
            },
            SpatialLayerId = spatial,
            MaxSpatialLayerId = 2,
            TemporalLayerId = temporal,
            KeyFrameNumber = keyFrameNumber,
        };

    private static Func<VideoFrame?> Mutate(Action action)
        => () => {
            action();
            return null;
        };

    private static async IAsyncEnumerable<VideoFrame> Frames(params object[] items)
    {
        foreach (var item in items) {
            if (item is VideoFrame frame)
                yield return frame;
            else if (item is Func<VideoFrame?> mutate)
                _ = mutate();
            await Task.Yield();
        }
    }
}
