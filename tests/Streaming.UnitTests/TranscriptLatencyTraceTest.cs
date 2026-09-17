using System.Numerics;
using ActualChat.Transcription;
using ActualLab.Time.Testing;

namespace ActualChat.Streaming.UnitTests;

public class TranscriptLatencyTraceTest
{
    private static readonly Moment RecordedAt = new(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void LagsShouldBeMeasuredBehindTheSpeech()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new TranscriptLatencyTrace("s1", RecordedAt, clock);

        // act
        // 1.5 s in, Soniox has text up to 0.6 s of speech: 0.9 s behind
        clock.SetTo(RecordedAt + TimeSpan.FromSeconds(1.5));
        trace.OnTranscript(Unstable("Hello", 0.6f));
        // 4.0 s in, the finals cover 0.8 s: 3.2 s behind
        clock.SetTo(RecordedAt + TimeSpan.FromSeconds(4.0));
        trace.OnTranscript(Stable("Hello", 0.8f) with { IsSegmentEnd = true });
        clock.SetTo(RecordedAt + TimeSpan.FromSeconds(4.5));
        trace.OnTranscript(Unstable("Hello world", 3.1f));

        // assert
        trace.FirstTextDelay.Should().Be(TimeSpan.FromSeconds(1.5));
        trace.FirstTextSourceEnd.Should().BeApproximately(0.6f, 0.01f);
        trace.Text.Count.Should().Be(2);
        trace.Text.First.Should().BeCloseTo(TimeSpan.FromSeconds(0.9), TimeSpan.FromMilliseconds(10));
        trace.Text.Max.Should().BeCloseTo(TimeSpan.FromSeconds(1.4), TimeSpan.FromMilliseconds(10));
        trace.Stable.Count.Should().Be(1);
        trace.Stable.First.Should().BeCloseTo(TimeSpan.FromSeconds(3.2), TimeSpan.FromMilliseconds(10));
        trace.SegmentEndCount.Should().Be(1);
        trace.ToString().Should().Be(
            "Transcript latency #s1: first text +1.5s at 0.6s of speech; "
            + "text lag p50 1.4s max 1.4s (n=2); stable lag p50 3.2s max 3.2s (n=1); 1 segment ends");
    }

    [Fact]
    public void EmptyTranscriptsShouldNotCount()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt + TimeSpan.FromSeconds(1));
        var trace = new TranscriptLatencyTrace("s1", RecordedAt, clock);

        // act
        trace.OnTranscript(Unstable("", 0f));
        trace.OnTranscript(Stable("", 0f));

        // assert
        trace.Text.Count.Should().Be(0);
        trace.FirstTextDelay.Should().BeNull();
    }

    [Fact]
    public async Task WithLatencyTraceShouldStampLagsOnArrivalNotAtStreamEnd()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new TranscriptLatencyTrace("s1", RecordedAt, clock);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        var channel = Channel.CreateUnbounded<Transcript>();
        var traced = AudioStreamingBackend.WithLatencyTrace(
            channel.Reader.ReadAllAsync(), trace, logger.Object, CancellationToken.None);

        // act: each write is stamped as soon as it's pulled through, not when the channel completes
        await using var enumerator = traced.GetAsyncEnumerator();
        clock.SetTo(RecordedAt + TimeSpan.FromSeconds(1.5));
        await channel.Writer.WriteAsync(Unstable("Hello", 0.6f));
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        clock.SetTo(RecordedAt + TimeSpan.FromSeconds(10));
        await channel.Writer.WriteAsync(Stable("Hello", 0.8f));
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        channel.Writer.Complete();
        (await enumerator.MoveNextAsync()).Should().BeFalse();

        // assert
        trace.Text.First.Should().BeCloseTo(TimeSpan.FromSeconds(0.9), TimeSpan.FromMilliseconds(10));
        trace.Stable.First.Should().BeCloseTo(TimeSpan.FromSeconds(9.2), TimeSpan.FromMilliseconds(10));
        logger.Verify(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    private static Transcript Unstable(string text, float endSeconds)
        => new(text, new LinearMap(Vector2.Zero, new Vector2(text.Length, endSeconds)), []);

    private static Transcript Stable(string text, float endSeconds)
        => Unstable(text, endSeconds) with { IsStable = true };
}
