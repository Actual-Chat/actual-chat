using ActualChat.Audio;
using ActualChat.Audio.Ogg;
using ActualChat.Testing.Audio;

namespace ActualChat.Transcription.UnitTests;

public class OpusFramePacerTest
{
    [Fact]
    public async Task PacerShouldEmitContiguousFramesForCompleteInput()
    {
        // arrange
        var pacer = new OpusFramePacer(MomentClockSet.Default.CpuClock);
        var input = Channel.CreateUnbounded<AudioFrame>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        foreach (var frame in OggOpusTestStream.Frames(3, firstIndex: 100))
            input.Writer.TryWrite(frame);
        input.Writer.Complete();

        // act
        await pacer.Run(input.Reader, output.Writer, CancellationToken.None);
        var result = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(3);
        for (var i = 0; i < result.Count; i++) {
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i, "output offsets run from zero");
            result[i].Data.ToArray().Should().Equal(OggOpusTestStream.Packet(100 + i));
        }
        result.Should().OnlyContain(f => f.Duration == Constants.Audio.OpusFrameDuration);
    }

    [Fact]
    public async Task PacerShouldFillGapsWithSilence()
    {
        // arrange
        var pacer = new OpusFramePacer(MomentClockSet.Default.CpuClock);
        var input = Channel.CreateUnbounded<AudioFrame>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        var runTask = pacer.Run(input.Reader, output.Writer, CancellationToken.None);

        // act
        await Task.Delay(200);
        input.Writer.TryWrite(OggOpusTestStream.Frames(1)[0]);
        input.Writer.Complete();
        await runTask;
        var result = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Count.Should().BeGreaterThanOrEqualTo(6,
            "200ms of waiting is 10 silence frames minus scheduling slack, plus the real one");
        for (var i = 0; i < result.Count; i++) {
            result[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
            var expected = i < result.Count - 1 ? OpusFramePacer.SilencePacket : OggOpusTestStream.Packet(0);
            result[i].Data.ToArray().Should().Equal(expected);
        }
    }

    [Fact]
    public async Task PacerShouldEmitAtWallClockPace()
    {
        // arrange
        var pacer = new OpusFramePacer(MomentClockSet.Default.CpuClock);
        var input = Channel.CreateUnbounded<AudioFrame>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        foreach (var frame in OggOpusTestStream.Frames(15))
            input.Writer.TryWrite(frame);
        input.Writer.Complete();

        // act
        var startedAt = CpuTimestamp.Now;
        await pacer.Run(input.Reader, output.Writer, CancellationToken.None);
        var elapsed = startedAt.Elapsed;
        var result = await output.Reader.ReadAllAsync().ToListAsync();

        // assert
        result.Should().HaveCount(15);
        elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(15 * 20 - 30),
            "15 frames of 20 ms are emitted no faster than real time");
    }

    [Fact]
    public async Task PacerShouldPropagateTheProducersError()
    {
        // arrange
        var pacer = new OpusFramePacer(MomentClockSet.Default.CpuClock);
        var input = Channel.CreateUnbounded<AudioFrame>();
        var output = Channel.CreateUnbounded<AudioFrame>();
        input.Writer.Complete(new InvalidOperationException("tts died"));

        // act
        var act = () => pacer.Run(input.Reader, output.Writer, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("tts died");
        var readAll = () => output.Reader.ReadAllAsync().ToListAsync().AsTask();
        await readAll.Should().ThrowAsync<InvalidOperationException>("the output carries the same error");
    }

    [Fact]
    public void SilencePacketShouldBeASingle20MsFrame()
    {
        // act
        var packet = OpusFramePacer.SilencePacket;

        // assert
        packet.Should().NotBeEmpty();
        OggOpusReader.GetPacketDuration(packet).Should().Be(Constants.Audio.OpusFrameDuration);
    }
}
