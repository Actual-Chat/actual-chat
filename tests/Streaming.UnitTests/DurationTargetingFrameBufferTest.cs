using ActualChat.Media;

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

    [Fact]
    public void SkipUntilDropsBufferedFramesBeforeSourceOffset()
    {
        var buffer = NewBuffer();
        buffer.Push(Frame(0));
        buffer.Push(Frame(20));
        buffer.Push(Frame(40));
        buffer.Push(Frame(60));

        buffer.SkipUntil(TimeSpan.FromMilliseconds(50));

        buffer.TryRead(out var frame).Should().BeTrue();
        frame!.Offset.Should().Be(TimeSpan.FromMilliseconds(60));
        buffer.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void SpeedUpUntilDropsEveryNthFrameUntilSourceOffset()
    {
        var buffer = NewBuffer();
        for (var i = 0; i < 6; i++)
            buffer.Push(Frame(i * 20));

        buffer.SpeedUpUntil(TimeSpan.FromMilliseconds(100), 2);

        ReadOffsets(buffer).Should().Equal(
            TimeSpan.FromMilliseconds(0),
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void SkipUntilCancelsSpeedUp()
    {
        var buffer = NewBuffer();
        for (var i = 0; i < 6; i++)
            buffer.Push(Frame(i * 20));

        buffer.SpeedUpUntil(TimeSpan.FromMilliseconds(100), 2);
        buffer.SkipUntil(TimeSpan.FromMilliseconds(50));

        ReadOffsets(buffer).Should().Equal(
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromMilliseconds(100));
    }

    private static DurationTargetingFrameBuffer<TestFrame> NewBuffer(TimeSpan targetDuration = default)
        => new(static frame => frame.Offset, static frame => frame.Duration, targetDuration);

    private static TestFrame Frame(int offsetMs)
        => new(TimeSpan.FromMilliseconds(offsetMs), TimeSpan.FromMilliseconds(20));

    private static List<TimeSpan> ReadOffsets(DurationTargetingFrameBuffer<TestFrame> buffer)
    {
        var offsets = new List<TimeSpan>();
        while (buffer.TryRead(out var frame))
            offsets.Add(frame!.Offset);
        return offsets;
    }

    private sealed record TestFrame(TimeSpan Offset, TimeSpan Duration);
}
