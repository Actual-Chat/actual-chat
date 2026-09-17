using ActualChat.Audio;
using ActualChat.Testing.Audio;
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
        // arrange - 4 frames of original with offsets that neither start at zero nor stay contiguous
        using var clock = new TestClock(multiplier: 0);
        var offsets = new[] { 100, 120, 160, 180 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray();
        var original = OpusFrames(offsets.Length, 4000)
            .Select((frame, i) => frame with { Offset = offsets[i] })
            .ToList()
            .AsAsyncEnumerable()
            .Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();

        // act
        mix.DubPcm.TryComplete();
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(4);
        frames.Select(x => x.Offset).Should().Equal(offsets, "the original's offsets pass through verbatim");
        AudioFrameRms.Of(frames, 1..).Should().BeGreaterThan(2000, "the original passes at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task WhenCaughtUpShouldCompleteOnceTheBufferedOriginalIsReplayed()
    {
        // arrange - a live original with a header and 3 frames already buffered when the mix starts
        using var clock = new TestClock(multiplier: 0);
        var source = Channel.CreateUnbounded<AudioFrame>();
        source.Writer.TryWrite(new AudioFrame { Data = Array.Empty<byte>(), Offset = TimeSpan.FromMilliseconds(-1) });
        var frames = OpusFrames(5, 4000);
        foreach (var frame in frames.Take(3))
            source.Writer.TryWrite(frame);
        var original = source.Reader.Memoize();
        for (var i = 0; i < 100 && original.ProducedCount < 4; i++)
            await Task.Delay(10);
        original.ProducedCount.Should().Be(4);
        var mix = new VoiceOverMix(original, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryComplete();

        // act
        var runTask = mix.Run(output.Writer, CancellationToken.None);
        var caughtUpFrameCount = await mix.WhenCaughtUp.WaitAsync(TimeSpan.FromSeconds(5));
        var isRunCompletedAtCatchUp = runTask.IsCompleted;
        foreach (var frame in frames.Skip(3))
            source.Writer.TryWrite(frame);
        source.Writer.Complete();
        await runTask;

        // assert
        caughtUpFrameCount.Should().Be(3, "the header is replayed but not mixed");
        isRunCompletedAtCatchUp.Should().BeFalse("the mix goes on following the live original");
        output.Reader.Count.Should().Be(5);
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
        AudioFrameRms.Of(frames, 1..).Should().BeGreaterThan(2000,
            "RMS is what proves the 48 kHz decode: a 16 kHz decode would fill only a third of each frame");
    }

    [Fact(Timeout = 20_000)]
    public async Task DubShouldBeMixedInAndTheTailDrainedOnTheTick()
    {
        // arrange - 5 frames of original, 10 frames of a quiet dub arriving at once
        var original = OpusFrames(5, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryWrite(Pcm(10, 500));
        mix.DubPcm.TryComplete();

        // act - the tail ticks on the clock, 20 ms per frame
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - 5 mixed + 5 dub-only frames, the tail's offsets continuing the original's
        frames.Should().HaveCount(10);
        frames.Select(x => x.Offset).Should().Equal(Enumerable.Range(0, 10).Select(i => FrameDuration * i));
        frames[5].Offset.Should().Be(frames[4].Offset + FrameDuration, "the tail continues the last original offset");
        // Opus' first frame after the encoder starts is quieter (lookahead), so frame 1 is the reference
        AudioFrameRms.Of(frames, 1..2).Should().BeGreaterThan(AudioFrameRms.Of(frames, 4..5) * 1.5,
            "the original is ducked once the dub has ramped in");
        AudioFrameRms.Of(frames, 5..).Should().BeInRange(200, 600, "the tail is the dub alone, at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task MixShouldWaitForALateDubAfterTheOriginalEnded()
    {
        // arrange
        var original = OpusFrames(2, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        var runTask = mix.Run(output.Writer, CancellationToken.None);
        await WhenEmitted(output, 2);
        await Task.Delay(100);

        // act - nothing beyond the original is emitted while the dub is pending; then it arrives
        var pendingCount = output.Reader.Count;
        var isRunCompletedWhilePending = runTask.IsCompleted;
        mix.DubPcm.TryWrite(Pcm(1, 8000));
        mix.DubPcm.TryComplete();
        await runTask;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        pendingCount.Should().Be(2);
        isRunCompletedWhilePending.Should().BeFalse("the mix waits for the dub channel to complete");
        frames.Should().HaveCount(3);
        frames[2].Offset.Should().Be(FrameDuration * 2, "the dub tail continues the original's offsets");
    }

    [Fact(Timeout = 20_000)]
    public async Task TailShouldBePacedAfterALateDub()
    {
        // arrange - the dub arrives 300 ms after the original ended, all 10 frames at once
        var original = OpusFrames(2, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        var runTask = mix.Run(output.Writer, CancellationToken.None);
        await WhenEmitted(output, 2);
        await Task.Delay(300);

        // act
        var startedAt = CpuTimestamp.Now;
        mix.DubPcm.TryWrite(Pcm(10, 8000));
        mix.DubPcm.TryComplete();
        await runTask;
        var elapsed = startedAt.Elapsed;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - the held-back schedule is rebased, not emitted as a burst
        frames.Should().HaveCount(12);
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150),
            "10 tail frames are 9 ticks of 20 ms, minus scheduling slack");
    }

    [Fact(Timeout = 20_000)]
    public async Task FaultedDubShouldEndTheTailWithTheOriginalIntact()
    {
        // arrange - the mix is parked waiting for dub audio when the synthesizer fails
        var original = OpusFrames(2, 4000).AsAsyncEnumerable().Memoize();
        var mix = new VoiceOverMix(original, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        var runTask = mix.Run(output.Writer, CancellationToken.None);
        await WhenEmitted(output, 2);
        await Task.Delay(100);

        // act
        mix.DubPcm.TryComplete(new InvalidOperationException("tts down"));
        await runTask;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - Run completed cleanly, the output isn't faulted, the original is all there is
        frames.Should().HaveCount(2);
        output.Reader.Completion.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact(Timeout = 20_000)]
    public async Task NoOriginalShouldGiveADubOnlyMix()
    {
        // arrange
        var mix = new VoiceOverMix(null, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryWrite(Pcm(3, 8000));
        mix.DubPcm.TryComplete();

        // act
        await mix.Run(output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(3);
        frames.Select(x => x.Offset).Should().Equal(Enumerable.Range(0, 3).Select(i => FrameDuration * i));
        AudioFrameRms.Of(frames, 1..).Should().BeGreaterThan(4000, "the dub passes at full gain");
    }

    [Fact(Timeout = 20_000)]
    public async Task StrayTrailingByteShouldNotHoldTheTail()
    {
        // arrange - two frames of dub and one byte that can never become a sample
        var mix = new VoiceOverMix(null, new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryWrite(Pcm(2, 8000));
        mix.DubPcm.TryWrite(new byte[1]);
        mix.DubPcm.TryComplete();

        // act
        await mix.Run(output.Writer, CancellationToken.None);

        // assert
        output.Reader.Count.Should().Be(2);
    }

    [Fact(Timeout = 20_000)]
    public async Task NoOriginalAndNoDubShouldEndAtOnce()
    {
        // arrange
        using var clock = new TestClock(multiplier: 0);
        var mix = new VoiceOverMix(null, new DubActivity(), new MomentClockSet(clock), Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryComplete();

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
        AudioFrameRms.Of(frames, 4..).Should().BeLessThan(1500, "the original is held at the duck gain");
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
    public async Task DuckShouldBeReleasedOnceTheDubAndItsHoldArePast()
    {
        // arrange - 100 frames of original arriving at their own pace, 5 frames of dub at the start
        var frames = OpusFrames(100, 4000);
        var plain = await MixAlone(frames);
        var mix = new VoiceOverMix(Paced(frames).Memoize(), new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryWrite(Pcm(5, 500));
        mix.DubPcm.TryComplete();

        // act
        await mix.Run(output.Writer, CancellationToken.None);
        var mixed = await output.Reader.ReadAllAsync().ToListAsync();

        // assert - the hold (50 frames) past the last dub frame keeps the duck on, then it is released
        mixed.Should().HaveCount(100);
        AudioFrameRms.Of(mixed, 10..50).Should().BeLessThan(AudioFrameRms.Of(plain, 10..50) * 0.5,
            "the original is ducked while the dub speaks and for the hold after it");
        AudioFrameRms.Of(mixed, 60..).Should().BeGreaterThan(AudioFrameRms.Of(plain, 60..) * 0.9,
            "the activity the mix marks must not feed its own duck back to it past the hold");
    }

    [Fact(Timeout = 20_000)]
    public async Task NextUtteranceShouldBeDuckedOnlyWhileThePreviousDubDrains()
    {
        // arrange - one activity: A is a dub-only tail of 5 frames, B is 100 paced original frames
        var activity = new DubActivity();
        var mixA = new VoiceOverMix(null, activity, TickingClocks, Log);
        var outputA = Channel.CreateUnbounded<AudioFrame>();
        mixA.DubPcm.TryWrite(Pcm(5, 8000));
        mixA.DubPcm.TryComplete();
        var frames = OpusFrames(100, 4000);
        var plain = await MixAlone(frames);
        var outputB = Channel.CreateUnbounded<AudioFrame>();

        // act - B starts while A speaks
        var runATask = mixA.Run(outputA.Writer, CancellationToken.None);
        await WhenEmitted(outputA, 1);
        var mixB = new VoiceOverMix(Paced(frames).Memoize(), activity, TickingClocks, Log);
        mixB.DubPcm.TryComplete();
        await mixB.Run(outputB.Writer, CancellationToken.None);
        await runATask;
        var mixed = await outputB.Reader.ReadAllAsync().ToListAsync();

        // assert - B is ducked through A's tail (100 ms) and hold (1 s), at full gain after the ramp
        mixed.Should().HaveCount(100);
        AudioFrameRms.Of(mixed, 5..30).Should().BeLessThan(AudioFrameRms.Of(plain, 5..30) * 0.5,
            "the next utterance starts ducked while the previous dub is still speaking");
        AudioFrameRms.Of(mixed, 70..).Should().BeGreaterThan(AudioFrameRms.Of(plain, 70..) * 0.9,
            "a mix without a dub of its own never extends the duck the previous one handed it");
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

    private static async Task WhenEmitted(Channel<AudioFrame> output, int frameCount)
    {
        for (var i = 0; i < 200 && output.Reader.Count < frameCount; i++)
            await Task.Delay(10);
        output.Reader.Count.Should().Be(frameCount);
    }

    private async Task<List<AudioFrame>> MixAlone(List<AudioFrame> frames)
    {
        // The reference for the level assertions: the same original through a mix with no dub
        var mix = new VoiceOverMix(frames.AsAsyncEnumerable().Memoize(), new DubActivity(), TickingClocks, Log);
        var output = Channel.CreateUnbounded<AudioFrame>();
        mix.DubPcm.TryComplete();
        await mix.Run(output.Writer, CancellationToken.None);
        return await output.Reader.ReadAllAsync().ToListAsync();
    }

    private static async IAsyncEnumerable<AudioFrame> Paced(List<AudioFrame> frames)
    {
        // One frame per 20 ms, as a live original arrives: the activity's hold is wall-clock time
        foreach (var frame in frames) {
            yield return frame;

            await Task.Delay(FrameDuration);
        }
    }

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
}
