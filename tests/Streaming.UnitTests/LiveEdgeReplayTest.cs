using ActualChat.Audio;

namespace ActualChat.Streaming.UnitTests;

public class LiveEdgeReplayTest
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact(Timeout = 10_000)]
    public async Task SkipToLiveYieldsHeaderThenOnlyFutureFrames()
    {
        // arrange
        var source = Channel.CreateUnbounded<AudioFrame>();
        source.Writer.TryWrite(Header());
        source.Writer.TryWrite(Frame(0));
        source.Writer.TryWrite(Frame(20));
        await using var memoizer = new AsyncMemoizer<AudioFrame>(source.Reader.ReadAllAsync());
        await WhenProduced(memoizer, 3);

        // act
        var resultTask = AudioStreamingBackend
            .SkipToLive(memoizer, CancellationToken.None)
            .ToListAsync()
            .AsTask();
        source.Writer.TryWrite(Frame(40));
        source.Writer.Complete();
        var result = await resultTask.WaitAsync(TestTimeout);

        // assert
        result.Select(f => f.Offset).Should().Equal(
            TimeSpan.FromMilliseconds(-1),
            TimeSpan.FromMilliseconds(40));
    }

    [Fact(Timeout = 10_000)]
    public async Task SkipToLiveEmitsHeaderOnceWhenNothingIsBuffered()
    {
        // arrange
        var source = Channel.CreateUnbounded<AudioFrame>();
        await using var memoizer = new AsyncMemoizer<AudioFrame>(source.Reader.ReadAllAsync());

        // act
        var resultTask = AudioStreamingBackend
            .SkipToLive(memoizer, CancellationToken.None)
            .ToListAsync()
            .AsTask();
        source.Writer.TryWrite(Header());
        source.Writer.TryWrite(Frame(0));
        source.Writer.Complete();
        var result = await resultTask.WaitAsync(TestTimeout);

        // assert
        result.Select(f => f.Offset).Should().Equal(
            TimeSpan.FromMilliseconds(-1),
            TimeSpan.Zero);
    }

    // Private methods

    private static AudioFrame Header()
        => new() { Data = new byte[] { 1 }, Offset = TimeSpan.FromMilliseconds(-1) };

    private static AudioFrame Frame(int offsetMs)
        => new() { Data = new byte[] { 2 }, Offset = TimeSpan.FromMilliseconds(offsetMs) };

    private static async Task WhenProduced(AsyncMemoizer<AudioFrame> memoizer, int count)
    {
        while (memoizer.ProducedCount < count)
            await Task.Delay(10);
    }
}
