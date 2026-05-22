using ActualChat.Streaming.Services;
using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class ReceiveQualityFilterTest
{
    private static readonly ReceiveQuality TopQuality = new(2);

    [Fact]
    public async Task LoweredCapSwitchesOnNextKeyframe()
    {
        var quality = TopQuality;
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

        result.Select(x => x.LayerId).Should().Equal((byte)2, (byte)2, (byte)0, (byte)0);
    }

    [Fact]
    public async Task LoweredCapKeepsForwardingCurrentLayerUntilKeyframe()
    {
        var quality = TopQuality;
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

        result.Select(x => x.LayerId).Should().Equal((byte)2, (byte)2, (byte)2, (byte)0, (byte)0);
    }

    [Fact]
    public async Task UpgradedCapKeepsForwardingCurrentLayerUntilKeyframe()
    {
        var quality = ReceiveQuality.Lowest;
        var frames = Frames(
            Key(0, 1),
            Delta(0, 1),
            Mutate(() => quality = TopQuality),
            Delta(0, 1),
            Delta(2, 1),
            Key(2, 2),
            Delta(2, 2));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => x.LayerId).Should().Equal((byte)0, (byte)0, (byte)0, (byte)2, (byte)2);
    }

    [Fact]
    public async Task PausedDropsEveryFrameAndRequiresKeyframeOnResume()
    {
        var quality = TopQuality;
        var frames = Frames(
            Key(2, 1),
            Delta(2, 1),
            Mutate(() => quality = ReceiveQuality.Paused),
            Key(2, 2),
            Delta(2, 2),
            Mutate(() => quality = TopQuality),
            Delta(2, 2),
            Key(2, 3),
            Delta(2, 3));

        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        result.Select(x => (int)x.LayerId).Should().Equal(2, 2, 2, 2);
        result.Select(x => x.KeyFrameIndex).Should().Equal(1, 1, 3, 3);
    }

    private static VideoFrame Key(byte layer, long keyFrameNumber)
        => Frame(layer, keyFrameNumber, isKeyFrame: true);

    private static VideoFrame Delta(byte layer, long keyFrameNumber)
        => Frame(layer, keyFrameNumber, isKeyFrame: false);

    private static VideoFrame Frame(byte layer, long keyFrameNumber, bool isKeyFrame)
        => new() {
            Width = layer switch {
                0 => 320,
                1 => 640,
                _ => 1280,
            },
            Height = layer switch {
                0 => 180,
                1 => 360,
                _ => 720,
            },
            LayerId = layer,
            LayerCount = 3,
            // KF: Index == KeyFrameIndex (so IsKeyFrame is true);
            // Delta: Index = -1 (or any other value != KeyFrameIndex) so the
            // getter returns false. The filter only ever compares KeyFrameIndex
            // values, so the delta's Index value itself is immaterial.
            KeyFrameIndex = (int)keyFrameNumber,
            Index = isKeyFrame ? (int)keyFrameNumber : -1,
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
