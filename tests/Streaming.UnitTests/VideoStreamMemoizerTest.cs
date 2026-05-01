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
