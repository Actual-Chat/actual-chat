using ActualChat.Audio;

namespace ActualChat.Transcription.UnitTests;

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
        await Task.Delay(200);
        pcm.Writer.TryWrite(new byte[OpusFramePump.FrameByteLength]);
        pcm.Writer.Complete();
        await runTask;
        var result = await frames.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Count.Should().BeGreaterThanOrEqualTo(6,
            "200ms of waiting is 10 silence frames minus scheduling slack, plus the real one");
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
