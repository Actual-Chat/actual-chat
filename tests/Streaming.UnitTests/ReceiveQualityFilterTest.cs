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

    [Fact]
    public async Task CollapseToRenumberedSingleLayerNeverMixesGops()
    {
        // arrange: demand-set collapse — the 360p tier moves from LayerId 1
        // (2-layer ladder) to LayerId 0 (1-layer ladder); the reshape starts
        // a fresh keyframe chain on the renumbered layer.
        var quality = new ReceiveQuality(1);
        var frames = Frames(
            Sized(0, 320, 180, 2, 1, isKeyFrame: true),
            Sized(1, 640, 360, 2, 1, isKeyFrame: true),
            Sized(1, 640, 360, 2, 1, isKeyFrame: false),
            Sized(0, 640, 360, 1, 2, isKeyFrame: true),
            Sized(0, 640, 360, 1, 2, isKeyFrame: false));

        // act
        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        // assert: 360p continues across the renumbering, the switch happens on
        // the reshape keyframe, and every delta follows its own chain's keyframe.
        result.Select(x => (int)x.Width).Should().Equal(640, 640, 640, 640);
        result.Select(x => (int)x.LayerId).Should().Equal(1, 1, 0, 0);
        result.Select(x => x.KeyFrameIndex).Should().Equal(1, 1, 2, 2);
        result.Select(x => x.IsKeyFrame).Should().Equal(true, false, true, false);
    }

    [Fact]
    public async Task ReAddingLowerTierReanchorsOnKeyframeWithoutGopMix()
    {
        // arrange: collapsed 1-layer stream (360p as LayerId 0), then the real
        // 180p L0 returns — 360p moves back to LayerId 1; the reshape emits
        // fresh keyframes on both layers in the same source moment.
        var quality = new ReceiveQuality(1);
        var frames = Frames(
            Sized(0, 640, 360, 1, 2, isKeyFrame: true),
            Sized(0, 640, 360, 1, 2, isKeyFrame: false),
            Sized(0, 320, 180, 2, 3, isKeyFrame: true),
            Sized(1, 640, 360, 2, 3, isKeyFrame: true),
            Sized(0, 320, 180, 2, 3, isKeyFrame: false),
            Sized(1, 640, 360, 2, 3, isKeyFrame: false));

        // act
        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        // assert: the viewer rides 360p through both ladder shapes, switches
        // only at the re-add keyframe, and never receives a 180p frame.
        result.Select(x => (int)x.Width).Should().Equal(640, 640, 640, 640);
        result.Select(x => (int)x.LayerId).Should().Equal(0, 0, 1, 1);
        result.Select(x => x.KeyFrameIndex).Should().Equal(2, 2, 3, 3);
    }

    [Fact]
    public async Task ForeignChainDeltaTripsGapDetectionUntilNextKeyframe()
    {
        // arrange: a delta whose KeyFrameIndex doesn't match the selected
        // chain (can't happen on the ordered wire; models corruption). The
        // filter must drop it AND everything after it until the next keyframe
        // rather than hand a cross-GOP delta to the decoder.
        var quality = new ReceiveQuality(1);
        var frames = Frames(
            Sized(0, 640, 360, 1, 2, isKeyFrame: true),
            Sized(0, 640, 360, 1, 2, isKeyFrame: false),
            Sized(0, 320, 180, 1, 1, isKeyFrame: false), // foreign-chain delta
            Sized(0, 640, 360, 1, 2, isKeyFrame: false), // dropped: skipping
            Sized(0, 640, 360, 1, 3, isKeyFrame: true),
            Sized(0, 640, 360, 1, 3, isKeyFrame: false));

        // act
        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        // assert
        result.Select(x => (int)x.Width).Should().Equal(640, 640, 640, 640);
        result.Select(x => x.KeyFrameIndex).Should().Equal(2, 2, 3, 3);
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

    private static VideoFrame Sized(
        byte layer, int width, int height, int layerCount, long keyFrameNumber, bool isKeyFrame)
        => new() {
            Width = width,
            Height = height,
            LayerId = layer,
            LayerCount = (byte)layerCount,
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
