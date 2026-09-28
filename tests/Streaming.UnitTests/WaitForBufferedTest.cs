using ActualChat.Audio;

namespace ActualChat.Streaming.UnitTests;

public sealed class WaitForBufferedTest
{
    [Fact(Timeout = 60_000)]
    public async Task WaitShouldEndWhenTheLastFrameArrivesMidCheck()
    {
        // A frame that lands between the wait's check and its subscription must still end the wait:
        // a finished recording's mix emits nothing more until the dub this wait holds back starts
        for (var i = 0; i < 20_000; i++) {
            // arrange
            var frames = Channel.CreateUnbounded<AudioFrame>();
            using var memoizer = frames.Reader.ReadAllAsync().Memoize();
            frames.Writer.TryWrite(new AudioFrame { Offset = TimeSpan.Zero });

            // act
            var waitTask = Task.Run(() => AudioStreamingBackend.WaitForBuffered(memoizer, 2, CancellationToken.None));
            frames.Writer.TryWrite(new AudioFrame { Offset = Constants.Audio.OpusFrameDuration });

            // assert
            var whenDone = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
            whenDone.Should().BeSameAs(waitTask, $"the wait saw both frames produced (iteration {i})");
        }
    }
}
