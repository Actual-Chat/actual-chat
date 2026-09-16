using ActualChat.Audio;
using ActualLab.Time.Testing;
using OpusSharp.Core;

namespace ActualChat.Streaming.UnitTests;

public class VoiceOverMixTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const double OriginalToneHz = 440;
    private const double DubToneHz = 660;
    private static readonly TimeSpan FrameDuration = Constants.Audio.OpusFrameDuration;
    // A frozen TestClock can't Delay (it divides the due time by its multiplier), so the tests
    // whose dub tail ticks past the original run on the real clock: 20 ms per tail frame
    private static readonly MomentClockSet TickingClocks = MomentClockSet.Default;

    [Fact(Timeout = 20_000)]
    public async Task OriginalFramesShouldClockTheMixAndKeepTheirOffsets()
    {
        // arrange - 10 frames of original, no dub; the clock never advances
        using var clock = new TestClock(multiplier: 0);
        var original = OpusFrames(10, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();

        // act
        mix.DubPcm.TryComplete();
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(10);
        frames.Select(x => x.Offset).Should().Equal(Enumerable.Range(0, 10).Select(i => FrameDuration * i));
        Rms(frames, 1..).Should().BeGreaterThan(2000, "the original passes at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task RecordingRateOriginalShouldYieldOnePlaybackRateFramePerFrame()
    {
        // arrange - the original is a 16 kHz recording, the mix runs at 48 kHz
        using var clock = new TestClock(multiplier: 0);
        var original = RecordingOpusFrames(10, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();

        // act
        mix.DubPcm.TryComplete();
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(10);
        frames.Select(x => x.Offset).Should().Equal(Enumerable.Range(0, 10).Select(i => FrameDuration * i));
        using var decoder = new OpusToPcmDecoder(Constants.Audio.PlaybackSampleRate);
        foreach (var frame in frames)
            decoder.Decode(frame.Data.Span).Length.Should().Be(Constants.Audio.PcmFrameLength * sizeof(short),
                "every original frame becomes exactly one 20 ms frame at the playback rate");
        Rms(frames, 1..).Should().BeGreaterThan(2000, "the original passes at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task DubShouldBeMixedInAndTheTailDrainedOnTheTick()
    {
        // arrange - 5 frames of original, 10 frames of a quiet dub arriving at once
        var original = OpusFrames(5, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.OnSynthesisStarted();
        mix.DubPcm.TryWrite(Pcm(10, 500));
        mix.DubPcm.TryComplete();

        // act - the tail ticks on the clock, 20 ms per frame
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - 5 mixed + 5 dub-only frames, offsets contiguous
        frames.Should().HaveCount(10);
        frames.Select(x => x.Offset).Should().Equal(Enumerable.Range(0, 10).Select(i => FrameDuration * i));
        // Opus' first frame after the encoder starts is quieter (lookahead), so frame 1 is the reference
        Rms(frames, 1..2).Should().BeGreaterThan(Rms(frames, 4..5) * 1.5,
            "the original is ducked once the dub has ramped in");
        Rms(frames, 5..).Should().BeInRange(200, 600, "the tail is the dub alone, at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task MixShouldWaitForALateDubAfterTheOriginalEnded()
    {
        // arrange
        var original = OpusFrames(2, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.OnSynthesisStarted();
        var runTask = mix.Run(output.Writer, CancellationToken.None);
        await Task.Delay(200);

        // act - nothing beyond the original is emitted while the dub is pending; then it arrives
        var pendingCount = output.Reader.Count;
        mix.DubPcm.TryWrite(Pcm(1, 8000));
        mix.DubPcm.TryComplete();
        await runTask;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        pendingCount.Should().Be(2);
        frames.Should().HaveCount(3);
        frames[2].Offset.Should().Be(FrameDuration * 2, "the dub tail continues the original's offsets");
    }

    [Fact(Timeout = 20_000)]
    public async Task NoOriginalShouldGiveADubOnlyMix()
    {
        // arrange
        var mix = new VoiceOverMix(null, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.OnSynthesisStarted();
        mix.DubPcm.TryWrite(Pcm(3, 8000));
        mix.DubPcm.TryComplete();

        // act
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(3);
        Rms(frames, 1..).Should().BeGreaterThan(4000, "the dub passes at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task NoOriginalAndNoSynthesisShouldEndAtOnce()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0);
        var mix = new VoiceOverMix(null, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();

        // act
        await mix.Run(output.Writer, CancellationToken.None);

        // assert
        output.Reader.Completion.IsCompletedSuccessfully.Should().BeTrue();
        output.Reader.Count.Should().Be(0);
    }

    [Fact(Timeout = 20_000)]
    public async Task ActivityShouldDuckTheNextUtteranceFromItsFirstFrame()
    {
        // arrange - an activity that says "a dub is speaking" for the next 5 s
        using var clock = new TestClock(multiplier: 0);
        var activity = new DubActivity();
        activity.MarkSpeaking(clock.Now + TimeSpan.FromSeconds(5));
        var original = OpusFrames(6, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, activity, new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        var isDucked = false;
        mix.Ducked += () => isDucked = true;

        // act
        mix.DubPcm.TryComplete();
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - after the 50 ms ramp (frames 0-2) the original sits at a quarter
        isDucked.Should().BeTrue();
        Rms(frames, 4..).Should().BeLessThan(1500, "the original is held at the duck gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task MixShouldMarkTheActivityWhileTheDubSpeaks()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0);
        var activity = new DubActivity();
        var original = OpusFrames(2, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, activity, new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.OnSynthesisStarted();
        mix.DubPcm.TryWrite(Pcm(2, 8000));
        mix.DubPcm.TryComplete();
        var mixedCount = 0;
        mix.Mixed += () => mixedCount++;

        // act
        await mix.Run(output.Writer, CancellationToken.None);

        // assert
        mixedCount.Should().Be(1, "Mixed fires once, on the first frame");
        activity.IsSpeaking(clock.Now).Should().BeTrue();
        activity.SpeakingUntil.Should().Be(clock.Now + Constants.Audio.VoiceOverDuckHold);
    }

    [Fact(Timeout = 20_000)]
    public async Task RunShouldPropagateTheOriginalsFailure()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0);
        var source = Channel.CreateUnbounded<AudioFrame>();
        foreach (var frame in OpusFrames(2, 4000))
            source.Writer.TryWrite(frame);
        source.Writer.TryComplete(new InvalidOperationException("boom"));
        var mix = new VoiceOverMix(source.Reader.Memoize(), new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();

        // act
        var run = () => mix.Run(output.Writer, CancellationToken.None);
        var read = () => output.Reader.ReadAllAsync().ToListAsync().AsTask();

        // assert
        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await read.Should().ThrowAsync<InvalidOperationException>("the output carries the failure");
    }

    [Fact]
    public void DubActivityShouldOnlyMoveForward()
    {
        // arrange
        var activity = new DubActivity();
        var now = new Moment(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

        // act
        activity.MarkSpeaking(now + TimeSpan.FromSeconds(2));
        activity.MarkSpeaking(now + TimeSpan.FromSeconds(1));

        // assert
        activity.SpeakingUntil.Should().Be(now + TimeSpan.FromSeconds(2));
        activity.IsSpeaking(now + TimeSpan.FromSeconds(1.5)).Should().BeTrue();
        activity.IsSpeaking(now + TimeSpan.FromSeconds(2)).Should().BeFalse();
    }

    // Private methods

    private static List<AudioFrame> OpusFrames(int count, short amplitude)
    {
        using var encoder = OpusFramePump.NewEncoder();
        return OpusFrames(encoder, OpusFramePump.FrameLength, count, amplitude);
    }

    private static List<AudioFrame> RecordingOpusFrames(int count, short amplitude)
    {
        using var encoder = new OpusEncoder(
            Constants.Audio.RecordingSampleRate,
            Constants.Audio.Channels,
            OpusPredefinedValues.OPUS_APPLICATION_VOIP);
        return OpusFrames(encoder, Constants.Audio.OpusFrameLength, count, amplitude);
    }

    private static List<AudioFrame> OpusFrames(OpusEncoder encoder, int frameLength, int count, short amplitude)
    {
        var sampleRate = frameLength * 1000 / Constants.Audio.OpusFrameDurationMs;
        var pcm = new short[frameLength];
        var packet = new byte[4096];
        var frames = new List<AudioFrame>();
        for (var i = 0; i < count; i++) {
            Tone(pcm, amplitude, OriginalToneHz, sampleRate, i * frameLength);
            var length = encoder.Encode(pcm, frameLength, packet, packet.Length);
            frames.Add(new AudioFrame {
                Data = packet.AsSpan(0, length).ToArray(),
                Offset = FrameDuration * i,
            });
        }
        return frames;
    }

    private static byte[] Pcm(int frameCount, short amplitude)
    {
        var samples = new short[Constants.Audio.PcmFrameLength * frameCount];
        Tone(samples, amplitude, DubToneHz, Constants.Audio.PlaybackSampleRate, 0);
        return MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
    }

    private static void Tone(
        short[] samples,
        short amplitude,
        double hz,
        int sampleRate,
        int firstSampleIndex)
    {
        // Opus is a voice codec: it high-passes a constant away, so the levels are asserted on tones
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * hz * (firstSampleIndex + i) / sampleRate));
    }

    private static double Rms(List<AudioFrame> frames, System.Range range)
    {
        // Every frame is decoded in order - a decoder with no history under-delivers on its first
        // frame - and only the ones in range count
        var (offset, length) = range.GetOffsetAndLength(frames.Count);
        using var decoder = new OpusToPcmDecoder(Constants.Audio.PlaybackSampleRate);
        double sum = 0;
        var count = 0;
        for (var i = 0; i < frames.Count; i++) {
            var samples = MemoryMarshal.Cast<byte, short>(decoder.Decode(frames[i].Data.Span));
            if (i < offset || i >= offset + length)
                continue;

            foreach (var s in samples) {
                sum += (double)s * s;
                count++;
            }
        }
        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }
}
