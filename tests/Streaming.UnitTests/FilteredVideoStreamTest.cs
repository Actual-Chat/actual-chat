using ActualChat.Video;

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

    // Helpers

    private async Task<List<VideoFrame>> Filter(VideoFrame[] frames, CancellationToken cancellationToken = default)
    {
        var backend = CreateBackend();
        var source = ToAsyncEnumerable(frames, cancellationToken);
        return await backend
            .FilteredVideoStream(
                StreamId.New(new NodeRef("test-node"), "test-local"),
                "test-peer",
                TimeSpan.Zero,
                source,
                cancellationToken)
            .ToListAsync(cancellationToken);
    }

    private VideoStreamingBackend CreateBackend()
    {
        // Minimal service provider — only ILoggerFactory needed for the filter pipeline
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(new TestLoggerProvider(Log)).SetMinimumLevel(LogLevel.Debug))
            .BuildServiceProvider();

        return new VideoStreamingBackend(services);
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

    private static VideoFrame KeyFrame(long kfNumber, int temporalLayer = 0)
        => new(true) { KeyFrameNumber = kfNumber, TemporalLayerId = temporalLayer };

    private static VideoFrame Delta(long kf, int temporalLayer = 0)
        => new(false) { KeyFrameNumber = kf, TemporalLayerId = temporalLayer };
}
