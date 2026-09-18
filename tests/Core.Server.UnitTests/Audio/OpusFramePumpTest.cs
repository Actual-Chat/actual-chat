using ActualChat.Audio;

namespace ActualChat.Core.Server.UnitTests.Audio;

public class OpusFramePumpTest
{
    [Fact]
    public async Task PumpShouldEmitContiguousFramesForCompleteInput()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 3]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(3);
        result.Select(f => f.Offset).Should().Equal(
            TimeSpan.Zero, Constants.Audio.OpusFrameDuration, Constants.Audio.OpusFrameDuration * 2);
        result.Should().OnlyContain(f => f.Data.Length > 0);
        result.Should().OnlyContain(f => f.Duration == Constants.Audio.OpusFrameDuration);
    }

    [Fact]
    public async Task PumpShouldFillGapsWithSilence()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        var runTask = pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);

        // act
        // Reading the frames before any input exists is what proves they are synthesized silence;
        // a wall-clock wait would race the pump's own timer and count whatever landed first
        var result = new List<AudioFrame>();
        for (var i = 0; i < 6; i++)
            result.Add(await frames.Reader.ReadAsync());
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength]);
        pcm.Writer.Complete();
        await runTask;
        result.AddRange(await frames.Reader.ReadAllAsync().ToListAsync());

        // assert
        result.Count.Should().BeGreaterThanOrEqualTo(7, "6 silence frames plus the real one");
        for (var i = 0; i < result.Count; i++)
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
    }

    [Fact]
    public async Task PumpShouldPadTheTailFrame()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength + OpusFramePump.FrameByteLength / 2]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(2, "the half frame is padded with silence rather than dropped");
    }

    [Fact]
    public async Task PumpShouldNotLoseSamplesAcrossOddLengthChunkBoundary()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength + 1]);
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength - 1]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(2, "an odd-length chunk boundary must not drop or shift a sample");
    }

    [Fact]
    public async Task PumpShouldTerminateOnALoneTrailingByte()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.TryWrite(new byte[1]);
        pcm.Writer.Complete();

        // act
        await pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Count.Should().BeLessThanOrEqualTo(2,
            "an unpairable single byte contributes no sample of its own, so the run terminates " +
            "after at most one extra silence frame instead of looping forever");
    }

    [Fact]
    public async Task AnUnpacedPumpWaitsForInputInsteadOfFillingTheGapWithSilence()
    {
        // arrange
        var pcm = Channel.CreateUnbounded<byte[]>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock, isPaced: false);
        var producer = Task.Run(async () => {
            await Task.Delay(300);
            pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 10]);
            await Task.Delay(300);
            pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 10]);
            pcm.Writer.Complete();
        });

        // act
        await pump.Run(pcm.Reader, output.Writer, CancellationToken.None);
        await producer;
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(20, "a wait for the producer is not a gap in the speech");
        frames[^1].Offset.Should().Be(TimeSpan.FromMilliseconds(20 * 19));
    }

    [Fact]
    public async Task AnUnpacedPumpEncodesASecondOfPcmWithoutWaitingASecond()
    {
        // arrange
        var pcm = Channel.CreateUnbounded<byte[]>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock, isPaced: false);
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength * 50]);
        pcm.Writer.Complete();

        // act
        var startedAt = CpuTimestamp.Now;
        await pump.Run(pcm.Reader, output.Writer, CancellationToken.None);
        var frames = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        frames.Should().HaveCount(50);
        frames[^1].Offset.Should().Be(TimeSpan.FromMilliseconds(20 * 49));
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "no pacing delay");
    }

    [Fact]
    public async Task PumpShouldPropagateTheProducersError()
    {
        // arrange
        using var pump = new OpusFramePump(MomentClockSet.Default.CpuClock);
        var pcm = Channel.CreateUnbounded<byte[]>();
        var frames = Channel.CreateUnbounded<AudioFrame>();
        pcm.Writer.Complete(new InvalidOperationException("tts died"));

        // act
        var act = () => pump.Run(pcm.Reader, frames.Writer, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("tts died");
        var readAll = () => frames.Reader.ReadAllAsync().ToListAsync().AsTask();
        await readAll.Should().ThrowAsync<InvalidOperationException>("the output carries the same error");
    }
}
