using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class KeyFrameGapFilterTest(ILogger log)
{
    private ILogger Log { get; } = log;

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
    public async Task RespectsCanellation()
    {
        using var cts = new CancellationTokenSource();
        var frames = new[] {
            KeyFrame(1), Delta(kf: 1), Delta(kf: 1),
        };

        await cts.CancelAsync();

        var act = () => Filter(frames, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // Helpers

    private async Task<List<VideoFrame>> Filter(VideoFrame[] frames, CancellationToken cancellationToken = default)
    {
        var source = ToAsyncEnumerable(frames, cancellationToken);
        return await VideoStreamingBackend
            .KeyFrameGapFilter(source, Log, cancellationToken)
            .ToListAsync(cancellationToken);
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

    private static VideoFrame KeyFrame(long kfNumber) => new(true) { KeyFrameNumber = kfNumber };
    private static VideoFrame Delta(long kf) => new(false) { KeyFrameNumber = kf };
}
