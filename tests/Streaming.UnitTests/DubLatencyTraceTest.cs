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
        At(clock, 3.1); trace.OnMixed(); // the mix's first frame, 0.1 s after the request
        At(clock, 3.2); trace.OnSourceReady(4.9f);
        At(clock, 3.4); trace.OnDecided(true);
        At(clock, 5.0); trace.OnTranslated(Unstable("Hola", 1.0f)); // ignored: not stable
        At(clock, 5.9); trace.OnTranslated(Stable("Hola mundo", 2.0f)); // 3.9 s behind
        At(clock, 5.9); trace.OnSpoken(Stable("Hola mundo", 2.0f)); // 3.9 s behind
        At(clock, 6.4); listener.OnStreamOpened(); // tts opened 0.5 s after the first spoken chunk
        At(clock, 7.5); listener.OnAudioStarted(); // tts 1.1 s; first word 7.5 - 2.0 = 5.5 s
        At(clock, 7.6); trace.OnDucked(); // the original goes under the dub 7.6 s into the speech
        At(clock, 9.0); trace.OnTranslated(Stable("Hola mundo otra vez", 4.4f)); // 4.6 s behind
        At(clock, 9.0); trace.OnSpoken(Stable("Hola mundo otra vez", 4.4f));
        At(clock, 12.0); listener.OnStreamOpened(); // a rollover: second stream
        At(clock, 13.2); listener.OnAudioStarted(); // 1.2 s, first word unchanged
        At(clock, 13.3); trace.OnDucked(); // a later duck doesn't move the first

        // assert
        trace.DecisionDelay.Should().Be(TimeSpan.FromSeconds(0.4));
        trace.MixDelay.Should().BeCloseTo(TimeSpan.FromSeconds(0.1), TimeSpan.FromMilliseconds(10));
        trace.TtsOpenDelay.Should().Be(TimeSpan.FromSeconds(0.5));
        trace.Translated.Count.Should().Be(2);
        trace.Translated.First.Should().BeCloseTo(TimeSpan.FromSeconds(3.9), TimeSpan.FromMilliseconds(10));
        trace.Translated.Max.Should().BeCloseTo(TimeSpan.FromSeconds(4.6), TimeSpan.FromMilliseconds(10));
        trace.Spoken.Count.Should().Be(2);
        trace.TtsFirstAudio.Count.Should().Be(2);
        trace.TtsFirstAudio.First.Should().Be(TimeSpan.FromSeconds(1.1));
        trace.FirstWordLag.Should().BeCloseTo(TimeSpan.FromSeconds(5.5), TimeSpan.FromMilliseconds(10));
        trace.ToString().Should().Be(
            "Dub latency #node01-abc~en: decided +0.4s; requested at 4.9s of speech; "
            + "translated lag p50 4.6s max 4.6s (n=2); spoken lag p50 4.6s max 4.6s (n=2); "
            + "tts opened +0.5s after the first chunk; tts first audio p50 1.2s max 1.2s (n=2); "
            + "first word 5.5s behind speech; mixed +0.1s; ducked at 7.6s of speech; voice stock");
    }

    [Fact]
    public void MixThatNeverDucksShouldSaySo()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);

        // act - a NoDub mix: the original alone, never ducked
        At(clock, 3.0); trace.OnRequested();
        At(clock, 3.0); trace.OnMixed();
        At(clock, 3.4); trace.OnDecided(false);

        // assert
        trace.MixDelay.Should().Be(TimeSpan.Zero);
        trace.ToString().Should().Contain("; mixed +0.0s; ducked -; ");
    }

    [Fact]
    public void LineBeforeAnyMixShouldShowBothAsMissing()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);

        // act
        trace.OnRequested();

        // assert
        trace.MixDelay.Should().BeNull();
        trace.ToString().Should().Contain("; mixed -; ducked -; ");
    }

    [Theory]
    [InlineData(null, "voice stock")]
    [InlineData("", "voice stock")]
    [InlineData("a1b2c3d4e5f6", "voice a1b2c3d4e5f6")]
    public void LineShouldNameTheVoice(string? voiceId, string expected)
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);

        // act
        trace.OnVoice(voiceId);

        // assert
        trace.ToString().Should().EndWith("; " + expected);
    }

    [Fact]
    public void KilledStreamShouldFoldIntoItsReplacementsFirstAudioSample()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0).SetTo(RecordedAt);
        var trace = new DubLatencyTrace(DubStreamId, RecordedAt, clock);
        ISpeechSynthesisListener listener = trace;

        // act
        At(clock, 5.9); trace.OnSpoken(Stable("Hola", 1.0f));
        At(clock, 5.9); listener.OnStreamOpened();
        At(clock, 8.0); listener.OnStreamOpened(); // re-open after a kill, no audio in between
        At(clock, 9.0); listener.OnAudioStarted();

        // assert
        trace.TtsOpenDelay.Should().Be(TimeSpan.Zero);
        trace.TtsFirstAudio.Count.Should().Be(1);
        trace.TtsFirstAudio.First.Should().Be(TimeSpan.FromSeconds(3.1));
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
