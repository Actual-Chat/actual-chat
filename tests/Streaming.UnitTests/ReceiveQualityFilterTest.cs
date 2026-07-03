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
    public async Task DroppingLowerTierKeepsSurvivingLayerChainUninterrupted()
    {
        // arrange: demand-set collapse {L0,L1} → {L1}. Layer ids are stable, so
        // the 360p viewer's chain continues right across the mask change —
        // no re-anchor, no keyframe wait.
        var quality = new ReceiveQuality(1);
        var frames = Frames(
            Sized(1, 640, 360, 0b11, 1, isKeyFrame: true),
            Sized(0, 320, 180, 0b11, 1, isKeyFrame: true),
            Sized(1, 640, 360, 0b11, 1, isKeyFrame: false),
            Sized(1, 640, 360, 0b10, 1, isKeyFrame: false), // L0 dropped mid-GOP
            Sized(1, 640, 360, 0b10, 2, isKeyFrame: true),
            Sized(1, 640, 360, 0b10, 2, isKeyFrame: false));

        // act
        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        // assert
        result.Select(x => (int)x.LayerId).Should().Equal(1, 1, 1, 1, 1);
        result.Select(x => x.KeyFrameIndex).Should().Equal(1, 1, 1, 2, 2);
    }

    [Fact]
    public async Task DisplacedViewerFallsUpToNearestLiveLayerAndBack()
    {
        // arrange: a thumbnail viewer (L0) whose tier is dropped (mask 0b10)
        // must move up to L1 on its keyframe, then return to L0 when it re-adds.
        var quality = new ReceiveQuality(0);
        var frames = Frames(
            Sized(0, 320, 180, 0b11, 1, isKeyFrame: true),
            Sized(0, 320, 180, 0b11, 1, isKeyFrame: false),
            Sized(1, 640, 360, 0b10, 5, isKeyFrame: true),
            Sized(1, 640, 360, 0b10, 5, isKeyFrame: false),
            Sized(0, 320, 180, 0b11, 9, isKeyFrame: true),
            Sized(1, 640, 360, 0b11, 8, isKeyFrame: false),
            Sized(0, 320, 180, 0b11, 9, isKeyFrame: false));

        // act
        var result = new List<VideoFrame>();
        await foreach (var frame in ReceiveQualityFilter
                           .Apply(frames, () => quality, NullLogger.Instance, CancellationToken.None))
            result.Add(frame);

        // assert: never frozen, never a cross-chain delta.
        result.Select(x => (int)x.Width).Should().Equal(320, 320, 640, 640, 320, 320);
        result.Select(x => x.KeyFrameIndex).Should().Equal(1, 1, 5, 5, 9, 9);
    }

    [Fact]
    public async Task SparseMaskServesEachConsumerItsExactLayer()
    {
        // arrange: producer encodes {L0, L2} (mask 0b101) for two different
        // consumers; a third asking for the missing L1 falls back to L0.
        var frames = new[] {
            Sized(0, 320, 180, 0b101, 1, isKeyFrame: true),
            Sized(2, 1280, 720, 0b101, 1, isKeyFrame: true),
            Sized(0, 320, 180, 0b101, 1, isKeyFrame: false),
            Sized(2, 1280, 720, 0b101, 1, isKeyFrame: false),
        };

        // act
        var byRequested = new Dictionary<int, List<int>>();
        foreach (var requested in new[] { 0, 1, 2 }) {
            var quality = new ReceiveQuality(requested);
            var result = new List<VideoFrame>();
            await foreach (var frame in ReceiveQualityFilter
                               .Apply(Frames(frames.Cast<object>().ToArray()), () => quality,
                                   NullLogger.Instance, CancellationToken.None))
                result.Add(frame);
            byRequested[requested] = result.Select(x => (int)x.LayerId).ToList();
        }

        // assert
        byRequested[0].Should().Equal(0, 0);
        byRequested[1].Should().Equal(0, 0); // nearest live layer below the hole
        byRequested[2].Should().Equal(2, 2);
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
            Sized(1, 640, 360, 0b10, 2, isKeyFrame: true),
            Sized(1, 640, 360, 0b10, 2, isKeyFrame: false),
            Sized(1, 640, 360, 0b10, 1, isKeyFrame: false), // foreign-chain delta
            Sized(1, 640, 360, 0b10, 2, isKeyFrame: false), // dropped: skipping
            Sized(1, 640, 360, 0b10, 3, isKeyFrame: true),
            Sized(1, 640, 360, 0b10, 3, isKeyFrame: false));

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
        byte layer, int width, int height, int layerMask, long keyFrameNumber, bool isKeyFrame)
        => new() {
            Width = width,
            Height = height,
            LayerId = layer,
            LayerCount = 3,
            LayerMask = (byte)layerMask,
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
