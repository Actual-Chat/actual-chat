namespace ActualChat.Streaming.UnitTests;

public class DurationTargetingFrameBufferTest
{
    [Fact]
    public void DoesNotReleaseUntilTargetDurationIsBuffered()
    {
        var buffer = NewBuffer(TimeSpan.FromMilliseconds(60));

        buffer.Push(Frame(0));
        buffer.Push(Frame(20));
        buffer.TryRead(out _).Should().BeFalse();

        buffer.Push(Frame(40));
        buffer.Duration.Should().Be(TimeSpan.FromMilliseconds(60));
        buffer.TryRead(out var frame).Should().BeTrue();
        frame!.Offset.Should().Be(TimeSpan.FromMilliseconds(0));

        buffer.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void CompleteReleasesFramesBelowTargetDuration()
    {
        var buffer = NewBuffer(TimeSpan.FromMilliseconds(60));
        buffer.Push(Frame(0));
        buffer.Push(Frame(20));

        buffer.Complete();

        buffer.TryRead(out var first).Should().BeTrue();
        first!.Offset.Should().Be(TimeSpan.FromMilliseconds(0));
        buffer.TryRead(out var second).Should().BeTrue();
        second!.Offset.Should().Be(TimeSpan.FromMilliseconds(20));
        buffer.TryRead(out _).Should().BeFalse();
    }

    private static DurationTargetingFrameBuffer<TestFrame> NewBuffer(TimeSpan targetDuration = default)
        => new(static frame => frame.Offset, static frame => frame.Duration, targetDuration);

    private static TestFrame Frame(int offsetMs)
        => new(TimeSpan.FromMilliseconds(offsetMs), TimeSpan.FromMilliseconds(20));

    private sealed record TestFrame(TimeSpan Offset, TimeSpan Duration);
}
