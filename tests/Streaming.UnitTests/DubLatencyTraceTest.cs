using System.Numerics;
using ActualChat.Transcription;
using ActualLab.Time.Testing;

namespace ActualChat.Streaming.UnitTests;

public class DubLatencyTraceTest
{
    private static readonly Moment RecordedAt = new(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));
    private static readonly StreamId DubStreamId = StreamId.Parse("node01-abc~en");

    [Fact]
    public void FirstWordShouldBeMeasuredFromTheFirstSpokenChunk()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);
        ISpeechSynthesisListener listener = trace;

        // act
        At(clock, 3.0); trace.OnRequested();
        At(clock, 3.4); trace.OnDecided();
        At(clock, 5.0); trace.OnTranslated(Unstable("Hola", 1.0f)); // ignored: not stable
        At(clock, 5.9); trace.OnTranslated(Stable("Hola mundo", 2.0f)); // 3.9 s behind
        At(clock, 5.9); trace.OnSpoken(Stable("Hola mundo", 2.0f)); // 3.9 s behind
        At(clock, 5.9); listener.OnStreamOpened();
        At(clock, 7.5); listener.OnAudioStarted(); // tts 1.6 s; first word 7.5 - 2.0 = 5.5 s
        At(clock, 9.0); trace.OnTranslated(Stable("Hola mundo otra vez", 4.4f)); // 4.6 s behind
        At(clock, 9.0); trace.OnSpoken(Stable("Hola mundo otra vez", 4.4f));
        At(clock, 12.0); listener.OnStreamOpened(); // a rollover: second stream
        At(clock, 13.2); listener.OnAudioStarted(); // 1.2 s, first word unchanged

        // assert
        trace.DecisionDelay.Should().Be(TimeSpan.FromSeconds(0.4));
        trace.Translated.Count.Should().Be(2);
        trace.Translated.First.Should().BeCloseTo(TimeSpan.FromSeconds(3.9), TimeSpan.FromMilliseconds(10));
        trace.Translated.Max.Should().BeCloseTo(TimeSpan.FromSeconds(4.6), TimeSpan.FromMilliseconds(10));
        trace.Spoken.Count.Should().Be(2);
        trace.TtsFirstAudio.Count.Should().Be(2);
        trace.TtsFirstAudio.First.Should().Be(TimeSpan.FromSeconds(1.6));
        trace.FirstWordLag.Should().BeCloseTo(TimeSpan.FromSeconds(5.5), TimeSpan.FromMilliseconds(10));
        trace.ToString().Should().Be(
            "Dub latency #node01-abc~en: decided +0.4s; translated lag p50 4.6s max 4.6s (n=2); "
            + "spoken lag p50 4.6s max 4.6s (n=2); tts first audio p50 1.6s max 1.6s (n=2); "
            + "first word 5.5s behind speech");
    }

    [Fact]
    public void AudioBeforeAnySpokenChunkShouldNotCrash()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt + TimeSpan.FromSeconds(1));
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);
        ISpeechSynthesisListener listener = trace;

        // act
        trace.OnRequested();
        listener.OnAudioStarted(); // no OnStreamOpened, no OnSpoken

        // assert
        trace.TtsFirstAudio.Count.Should().Be(0);
        trace.FirstWordLag.Should().BeNull();
        trace.ToString().Should().Contain("first word -");
    }

    private static void At(TestClock clock, double seconds)
        => clock.SetTo(RecordedAt + TimeSpan.FromSeconds(seconds));

    private static Transcript Unstable(string text, float endSeconds)
        => new(text, new LinearMap(Vector2.Zero, new Vector2(text.Length, endSeconds)), []);

    private static Transcript Stable(string text, float endSeconds)
        => Unstable(text, endSeconds) with { IsStable = true };
}
