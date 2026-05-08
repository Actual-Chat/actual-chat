using ActualChat.Video;

namespace ActualChat.Streaming.UnitTests;

public class VideoStreamMemoizerTest
{
    [Fact]
    public async Task InvalidTargetDurationDoesNotStartSource()
    {
        // arrange
        var source = new ProbeSource();

        // act
        var act = () => new VideoStreamMemoizer(source, TimeSpan.Zero);

        // assert
        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithParameterName("targetDuration");
        var completed = await Task
            .WhenAny(source.WhenMoved, Task.Delay(TimeSpan.FromMilliseconds(100)));
        completed.Should().NotBe(source.WhenMoved);
        source.MoveNextCount.Should().Be(0);
    }

    [Fact]
    public async Task ReplayStartsAtLatestKeyFrameSingleLayer()
    {
        // Two KFs — Replay must start at the latest, dropping the older span entirely.
        // Pre-KF deltas in front of the older KF are also dropped since their offset
        // sits below the start anchor.
        var frames = new[] {
            Key(layer: 0, offsetMs: 0),
            Delta(layer: 0, offsetMs: 33),
            Delta(layer: 0, offsetMs: 66),
            Key(layer: 0, offsetMs: 3000),
            Delta(layer: 0, offsetMs: 3033),
            Delta(layer: 0, offsetMs: 3066),
        };

        var result = await ReplayAfterIngest(frames, TimeSpan.FromSeconds(10));

        result.Should().HaveCount(3);
        result[0].Offset.Should().Be(TimeSpan.FromMilliseconds(3000));
        result[0].IsKeyFrame.Should().BeTrue();
        result.Last().Offset.Should().Be(TimeSpan.FromMilliseconds(3066));
    }

    [Fact]
    public async Task ReplayStartsAtMinAcrossAlignedLayers()
    {
        // Three layers, KFs perfectly aligned at the same source offsets.
        // MIN of latest-KF-per-layer == that aligned offset, so every layer's
        // latest KF is in the yielded prefix.
        var frames = new[] {
            Key(0, 0), Key(1, 0), Key(2, 0),
            Delta(0, 33), Delta(1, 33), Delta(2, 33),
            Key(0, 3000), Key(1, 3000), Key(2, 3000),
            Delta(0, 3033), Delta(1, 3033), Delta(2, 3033),
        };

        var result = await ReplayAfterIngest(frames, TimeSpan.FromSeconds(10));

        result.Where(f => f.IsKeyFrame).Should().HaveCount(3, "the 3 latest aligned KFs");
        result.Should().OnlyContain(f => f.Offset >= TimeSpan.FromMilliseconds(3000));
        result.Select(f => f.LayerId).Should().Contain([(byte)0, (byte)1, (byte)2]);
    }

    [Fact]
    public async Task ReplayStartsAtMinAcrossMisalignedLayers()
    {
        // Slight misalignment: layer 0's latest KF at 3000, layer 1's at 2950.
        // MIN = 2950, so both layers' latest KFs survive in the output.
        var frames = new[] {
            Key(0, 0), Key(1, 0),
            Delta(0, 33),
            Key(1, 2950),
            Delta(1, 2983),
            Key(0, 3000),
            Delta(0, 3033), Delta(1, 3033),
        };

        var result = await ReplayAfterIngest(frames, TimeSpan.FromSeconds(10));

        result.Should().OnlyContain(f => f.Offset >= TimeSpan.FromMilliseconds(2950));
        var keyFrames = result.Where(f => f.IsKeyFrame).ToList();
        keyFrames.Should().HaveCount(2);
        keyFrames.Should().Contain(f => f.LayerId == 0 && f.Offset == TimeSpan.FromMilliseconds(3000));
        keyFrames.Should().Contain(f => f.LayerId == 1 && f.Offset == TimeSpan.FromMilliseconds(2950));
    }

    [Fact]
    public async Task PausedLayerDoesNotPinReplayAnchor()
    {
        // Layer 2 emits one KF then stops. Layers 0 and 1 keep producing,
        // forcing eviction (targetDuration < buffered span). Inner-loop head
        // advance drains layer 2's KF queue to empty; the fix removes layer 2
        // from _latestKfByLayer so its frozen offset doesn't anchor Replay
        // backward into a span that no longer exists in the chain.
        var frames = new[] {
            Key(0, 0), Key(1, 0), Key(2, 0),
            Delta(0, 33), Delta(1, 33), Delta(2, 33),
            // Layer 2 pauses here — no more frames for it.
            Key(0, 1500), Key(1, 1500),
            Delta(0, 1533), Delta(1, 1533),
            Key(0, 3000), Key(1, 3000),
            Delta(0, 3033), Delta(1, 3033),
        };

        // targetDuration = 1s. After layer 0/1's KF at 1500 (kfs.Count=2,
        // buffered=1.5s > 1s) eviction fires; head advances past offset 1500.
        // Layer 2's only KF at 0 is popped from its queue → _latestKfByLayer
        // entry for layer 2 is removed.
        var result = await ReplayAfterIngest(frames, TimeSpan.FromSeconds(1));

        // Replay anchor = MIN of remaining latest-KFs (layers 0 and 1) = 3000.
        result.Should().OnlyContain(f => f.Offset >= TimeSpan.FromMilliseconds(3000));
        result.Should().OnlyContain(f => f.LayerId != 2);
        result.Where(f => f.IsKeyFrame).Should().HaveCount(2);
    }

    [Fact]
    public async Task ColdStartReplaysFromHeadWhenNoKeyFrameSeen()
    {
        // No keyframe emitted at all (cold edge case). _latestKfByLayer is empty,
        // override falls through to chain-head walk yielding everything.
        // The caller's SkipWhile(!IsKeyFrame) net handles the pre-KF span.
        var frames = new[] {
            Delta(0, 0),
            Delta(0, 33),
            Delta(0, 66),
        };

        var result = await ReplayAfterIngest(frames, TimeSpan.FromSeconds(10));

        result.Should().HaveCount(3);
        result.Should().OnlyContain(f => !f.IsKeyFrame);
    }

    [Fact]
    public async Task ReplayIgnoresTailSize()
    {
        // tailSize is documented as ignored on the override — anchor logic
        // alone drives the start position. Passing 1 must NOT clip the output
        // to the last item.
        var frames = new[] {
            Key(0, 0),
            Delta(0, 33),
            Key(0, 3000),
            Delta(0, 3033),
            Delta(0, 3066),
        };

        await using var memoizer = new VideoStreamMemoizer(
            SourceFrom(frames), TimeSpan.FromSeconds(10));
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));

        var withClip = await memoizer.Replay(tailSize: 1, default).ToListAsync();
        withClip.Should().HaveCount(3, "tailSize=1 must be ignored; anchor controls start");
        withClip[0].Offset.Should().Be(TimeSpan.FromMilliseconds(3000));
    }

    // Private methods

    private static async Task<List<VideoFrame>> ReplayAfterIngest(
        VideoFrame[] frames,
        TimeSpan targetDuration)
    {
        await using var memoizer = new VideoStreamMemoizer(
            SourceFrom(frames), targetDuration);
        await memoizer.WhenRunning!.WaitAsync(TimeSpan.FromSeconds(5));
        return await memoizer.Replay(int.MaxValue, default).ToListAsync();
    }

    private static async IAsyncEnumerable<VideoFrame> SourceFrom(VideoFrame[] frames)
    {
        foreach (var f in frames) {
            yield return f;
            await Task.Yield();
        }
    }

    private static VideoFrame Key(byte layer, int offsetMs)
        => Frame(layer, offsetMs, isKeyFrame: true);

    private static VideoFrame Delta(byte layer, int offsetMs)
        => Frame(layer, offsetMs, isKeyFrame: false);

    private static VideoFrame Frame(byte layer, int offsetMs, bool isKeyFrame)
        => new(isKeyFrame) {
            Offset = TimeSpan.FromMilliseconds(offsetMs),
            Duration = TimeSpan.FromMilliseconds(33),
            LayerId = layer,
        };

    // Nested types

    private sealed class ProbeSource : IAsyncEnumerable<VideoFrame>, IAsyncEnumerator<VideoFrame>
    {
        private readonly TaskCompletionSource _whenMoved = TaskCompletionSourceExt.New();

        public int MoveNextCount { get; private set; }
        public Task WhenMoved => _whenMoved.Task;
        public VideoFrame Current => new(false);

        public IAsyncEnumerator<VideoFrame> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => this;

        public ValueTask<bool> MoveNextAsync()
        {
            MoveNextCount++;
            _whenMoved.TrySetResult();
            return ValueTask.FromResult(false);
        }

        public ValueTask DisposeAsync()
            => default;
    }
}
