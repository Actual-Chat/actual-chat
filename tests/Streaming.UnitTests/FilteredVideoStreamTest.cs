using ActualChat.Streaming.Services;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.UnitTests;

public class FilteredVideoStreamTest(ILogger log)
{
    private ILogger Log { get; } = log;

    // --- KeyFrame gap detection (formerly KeyFrameGapFilterTest) ---

    [Fact]
    public async Task SkipsUntilFirstKeyFrame()
    {
        var frames = new[] {
            Delta(kf: 1), Delta(kf: 1), Delta(kf: 1),
            KeyFrame(2),
            Delta(kf: 2), Delta(kf: 2),
        };
        var result = await Filter(frames);

        result.Should().HaveCount(3);
        result[0].IsKeyFrame.Should().BeTrue();
        result[0].KeyFrameNumber.Should().Be(2);
    }

    [Fact]
    public async Task PassesThroughContiguousFrames()
    {
        var frames = new[] {
            KeyFrame(1),
            Delta(kf: 1), Delta(kf: 1), Delta(kf: 1),
            KeyFrame(2),
            Delta(kf: 2),
        };
        var result = await Filter(frames);

        result.Should().HaveCount(6, "all frames belong to contiguous keyframe groups");
    }

    [Fact]
    public async Task DetectsGapAndSkipsToNextKeyFrame()
    {
        var frames = new[] {
            KeyFrame(1),
            Delta(kf: 1),
            // Gap: frames from KF#2 were dropped, consumer sees KF#3 deltas
            Delta(kf: 3),
            Delta(kf: 3),
            KeyFrame(4),
            Delta(kf: 4),
        };
        var result = await Filter(frames);

        result.Should().HaveCount(4);
        result[0].KeyFrameNumber.Should().Be(1, "first keyframe group");
        result[1].KeyFrameNumber.Should().Be(1, "first delta");
        result[2].KeyFrameNumber.Should().Be(4, "recovery keyframe after gap");
        result[3].KeyFrameNumber.Should().Be(4, "delta after recovery");
    }

    [Fact]
    public async Task MultipleGapsRecoverEachTime()
    {
        var frames = new[] {
            KeyFrame(1),
            Delta(kf: 1),
            // Gap 1
            Delta(kf: 3),
            KeyFrame(4),
            Delta(kf: 4),
            // Gap 2
            Delta(kf: 7),
            KeyFrame(8),
        };
        var result = await Filter(frames);

        result.Select(f => f.KeyFrameNumber).Should().Equal(1, 1, 4, 4, 8);
    }

    [Fact]
    public async Task EmptyStream()
    {
        var result = await Filter([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyDeltaFrames_NeverYields()
    {
        var frames = new[] { Delta(kf: 1), Delta(kf: 2), Delta(kf: 3) };
        var result = await Filter(frames);

        result.Should().BeEmpty("no keyframe ever arrived");
    }

    [Fact]
    public async Task RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var frames = new[] {
            KeyFrame(1), Delta(kf: 1), Delta(kf: 1),
        };

        await cts.CancelAsync();

        var act = () => Filter(frames, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Temporal layer filtering ---

    [Fact]
    public async Task PassesAllTemporalLayersByDefault()
    {
        // Without latency state, maxLayer = int.MaxValue → all layers pass
        var frames = new[] {
            KeyFrame(1, temporalLayer: 0),
            Delta(kf: 1, temporalLayer: 1),
            Delta(kf: 1, temporalLayer: 2),
        };
        var result = await Filter(frames);

        result.Should().HaveCount(3, "all temporal layers should pass when no latency state");
    }

    // --- Combined: gap + temporal layer interaction ---

    [Fact]
    public async Task GapRecoveryWorksWithMixedTemporalLayers()
    {
        var frames = new[] {
            KeyFrame(1, temporalLayer: 0),
            Delta(kf: 1, temporalLayer: 0),
            Delta(kf: 1, temporalLayer: 1),
            // Gap
            Delta(kf: 3, temporalLayer: 0),
            KeyFrame(4, temporalLayer: 0),
            Delta(kf: 4, temporalLayer: 1),
        };
        var result = await Filter(frames);

        result.Should().HaveCount(5);
        result.Select(f => f.KeyFrameNumber).Should().Equal(1, 1, 1, 4, 4);
    }

    // --- Fast-join from retention ---
    // skipTo is advisory — the filter emits from the first keyframe it sees in the
    // replay. Blocking on skipTo would stall the consumer when the sender is idle
    // (retention held but no new frames advancing past skipTo).

    [Fact]
    public async Task FastJoin_SkipToIsAdvisory_EmitsFromFirstKeyFrameInRetention()
    {
        // Retention replay: two keyframes, skipTo lands after both.
        // Filter emits from KF#1 (the earliest keyframe it sees) and lets the
        // decoder catch up — the skipTo value does not delay emission.
        var frames = new[] {
            KeyFrame(1, offsetMs: 0),
            Delta(kf: 1, offsetMs: 33),
            KeyFrame(2, offsetMs: 2000),
            Delta(kf: 2, offsetMs: 2033),
            Delta(kf: 2, offsetMs: 2066),
            Delta(kf: 2, offsetMs: 2099),
        };
        var result = await Filter(frames, skipTo: TimeSpan.FromMilliseconds(2066));

        result.Should().HaveCount(6, "all frames from the first keyframe onward");
        result[0].IsKeyFrame.Should().BeTrue();
        result[0].KeyFrameNumber.Should().Be(1, "emit begins at the earliest keyframe, not at skipTo");
    }

    [Fact]
    public async Task FastJoin_IdleSender_StillEmitsFromRetainedKeyFrame()
    {
        // The bug this fix addresses: sender is idle, retention holds frames whose
        // offsets are all less than skipTo. Old filter waited for a frame with
        // offset >= skipTo that never arrived → 5s pull timeout. New filter emits
        // from the first keyframe in retention; receiver's decoder starts.
        var frames = new[] {
            KeyFrame(1, offsetMs: 0),
            Delta(kf: 1, offsetMs: 33),
            Delta(kf: 1, offsetMs: 66),
            Delta(kf: 1, offsetMs: 2000),  // last frame; sender went idle here
        };
        // skipTo far in the future (receiver's playback clock has advanced)
        var result = await Filter(frames, skipTo: TimeSpan.FromMilliseconds(10_000));

        result.Should().HaveCount(4, "filter must not block waiting for frames at skipTo");
        result[0].IsKeyFrame.Should().BeTrue();
    }

    [Fact]
    public async Task FastJoin_NoKeyFrameInRetention_WaitsForNextOne()
    {
        // Only P-frames in the replay; first keyframe arrives later.
        // Filter drops the leading deltas and starts emitting at the keyframe.
        var frames = new[] {
            Delta(kf: 1, offsetMs: 0),
            Delta(kf: 1, offsetMs: 33),
            Delta(kf: 1, offsetMs: 2066),
            KeyFrame(2, offsetMs: 2100),
            Delta(kf: 2, offsetMs: 2133),
        };
        var result = await Filter(frames, skipTo: TimeSpan.FromMilliseconds(2000));

        result.Should().HaveCount(2, "KF#2 + its trailing delta; pre-keyframe deltas are dropped");
        result[0].KeyFrameNumber.Should().Be(2);
        result[0].IsKeyFrame.Should().BeTrue();
    }

    [Fact]
    public async Task FastJoin_EmitsAllFramesFromFirstKeyFrameOnward()
    {
        // Screencast scenario: single keyframe, many P-frames, sparse encoder.
        // Filter emits everything from the keyframe so the decoder stays in sync.
        var frames = new[] {
            KeyFrame(1, offsetMs: 0),
            Delta(kf: 1, offsetMs: 100),
            Delta(kf: 1, offsetMs: 200),
            Delta(kf: 1, offsetMs: 300),
            Delta(kf: 1, offsetMs: 400),
        };
        var result = await Filter(frames, skipTo: TimeSpan.FromMilliseconds(300));

        result.Should().HaveCount(5);
        result[0].IsKeyFrame.Should().BeTrue();
        result.Select(f => f.Offset.TotalMilliseconds)
            .Should().Equal(0, 100, 200, 300, 400);
    }

    [Fact]
    public async Task FastJoin_MultipleKeyFrames_AllEmitted()
    {
        // Both keyframes and their P-frames are emitted in order. Decoder handles
        // the mid-stream keyframe as a fresh start — no discarding needed here.
        var frames = new[] {
            KeyFrame(1, offsetMs: 0),
            Delta(kf: 1, offsetMs: 100),
            Delta(kf: 1, offsetMs: 200),
            KeyFrame(3, offsetMs: 1000),
            Delta(kf: 3, offsetMs: 1100),
            Delta(kf: 3, offsetMs: 1200),
        };
        var result = await Filter(frames, skipTo: TimeSpan.FromMilliseconds(1200));

        result.Should().HaveCount(6, "both KFs and all their P-frames are passed through");
        result[0].KeyFrameNumber.Should().Be(1);
        result[3].KeyFrameNumber.Should().Be(3);
    }

    // Helpers

    private Task<List<VideoFrame>> Filter(VideoFrame[] frames, CancellationToken cancellationToken = default)
        => Filter(frames, TimeSpan.Zero, cancellationToken);

    private async Task<List<VideoFrame>> Filter(VideoFrame[] frames, TimeSpan skipTo, CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection()
            .AddSingleton<StreamLatencyStore>()
            .AddLogging(b => b.AddProvider(new TestLoggerProvider(Log)).SetMinimumLevel(LogLevel.Debug))
            .AddFusion(fusion => {
                fusion.AddService<VideoStreamingBackend>();
            })
            .BuildServiceProvider();

        var latencyStore = services.GetRequiredService<StreamLatencyStore>();
        var backend = services.GetRequiredService<VideoStreamingBackend>();
        var streamId = StreamId.New(new NodeRef("test-node"), "test-local");

        var filter = new VideoStreamFilter(
            latencyStore.GetPeerMaxTemporalLayer,
            (sid, ct) => Computed.Capture(() => backend.GetQualityPreset(sid, ct), ct),
            Log);

        var source = ToAsyncEnumerable(frames, cancellationToken);
        return await filter
            .Apply(streamId, "test-peer", skipTo, source, cancellationToken)
            .ToListAsync(cancellationToken);
    }

    private sealed class TestLoggerProvider(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    private static async IAsyncEnumerable<VideoFrame> ToAsyncEnumerable(
        VideoFrame[] frames,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in frames) {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return frame;
        }
    }

    private static VideoFrame KeyFrame(long kfNumber, int temporalLayer = 0, int offsetMs = 0)
        => new(true) {
            KeyFrameNumber = kfNumber,
            TemporalLayerId = temporalLayer,
            Offset = TimeSpan.FromMilliseconds(offsetMs),
        };

    private static VideoFrame Delta(long kf, int temporalLayer = 0, int offsetMs = 0)
        => new(false) {
            KeyFrameNumber = kf,
            TemporalLayerId = temporalLayer,
            Offset = TimeSpan.FromMilliseconds(offsetMs),
        };
}
