using ActualChat.Audio;
using ActualChat.Audio.Ogg;
using ActualChat.Testing.Audio;
using ActualLab.IO;

namespace ActualChat.Streaming.UnitTests;

public class OggOpusReaderTest(ILogger log)
{
    // Captured from Soniox TTS (audio_format: "opus"): 7 pages, OpusHead + OpusTags + 223 audio packets
    private const string FixtureName = "soniox-tts-sample.opus";
    private const int FixtureFrameCount = 223;
    private const int FixturePreSkip = 312;
    private const ulong FixtureLastGranule = 213_432;
    private const int SamplesPerFrame = 960;

    private ILogger Log { get; } = log;

    [Fact]
    public void ReaderShouldParseTheSonioxFixture()
    {
        // arrange
        var bytes = ReadFixture();
        var reader = new OggOpusReader();

        // act
        reader.Append(bytes);
        var frames = ReadAll(reader);

        // assert
        frames.Should().HaveCount(FixtureFrameCount);
        reader.FrameCount.Should().Be(FixtureFrameCount);
        reader.Head.Should().NotBeNull();
        reader.PreSkip.Should().Be(FixturePreSkip);
        reader.Head!.Value.OutputChannelCount.Should().Be(1);
        reader.Head!.Value.InputSampleRate.Should().Be(48_000);
        reader.IsEndOfStream.Should().BeTrue();
        reader.HasPendingData.Should().BeFalse();
        for (var i = 0; i < frames.Count; i++) {
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
            frames[i].Duration.Should().Be(Constants.Audio.OpusFrameDuration);
            frames[i].Data.Length.Should().BeGreaterThan(0);
        }
        // The last page's granule position (pre-skip + samples kept after end trimming) must account
        // for every frame: Soniox trims exactly the encoder delay it padded, i.e. under one frame
        var fullGranule = (ulong)(FixturePreSkip + FixtureFrameCount * SamplesPerFrame);
        reader.GranulePosition.Should().Be(FixtureLastGranule);
        reader.GranulePosition.Should().BeLessThanOrEqualTo(fullGranule);
        reader.GranulePosition.Should().BeGreaterThanOrEqualTo(fullGranule - SamplesPerFrame);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(47)]
    [InlineData(4096)]
    public async Task ReaderShouldYieldTheSameFramesForAnyChunking(int chunkSize)
    {
        // arrange
        var bytes = ReadFixture();
        var whole = new OggOpusReader();
        whole.Append(bytes);
        var expected = ReadAll(whole);
        var reader = new OggOpusReader();

        // act
        var frames = await reader.ReadFrames(Chunk(bytes, chunkSize), CancellationToken.None).ToListAsync();

        // assert
        frames.Should().HaveCount(expected.Count);
        for (var i = 0; i < frames.Count; i++) {
            frames[i].Offset.Should().Be(expected[i].Offset);
            frames[i].Data.ToArray().Should().Equal(expected[i].Data.ToArray());
        }
        reader.PreSkip.Should().Be(FixturePreSkip);
        reader.GranulePosition.Should().Be(FixtureLastGranule);
    }

    [Fact]
    public async Task ReaderShouldReadWhatTheWriterWrote()
    {
        // arrange
        var source = NewSyntheticSource(37, preSkip: 120);
        var converter = new OggOpusStreamConverter(new OggOpusStreamConverter.Options {
            PageDuration = TimeSpan.FromMilliseconds(200),
        });
        var byteStream = converter.ToByteStream(source, CancellationToken.None);
        var reader = new OggOpusReader();

        // act
        var frames = await reader.ReadFrames(byteStream, CancellationToken.None).ToListAsync();

        // assert
        frames.Should().HaveCount(37);
        reader.PreSkip.Should().Be(120);
        reader.IsEndOfStream.Should().BeTrue();
        for (var i = 0; i < frames.Count; i++) {
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
            frames[i].Data.ToArray().Should().Equal(OggOpusTestStream.Packet(i));
        }
    }

    [Fact]
    public async Task ConverterShouldReadAnOggOpusByteStreamIntoAnAudioSource()
    {
        // arrange
        var byteStream = GetAudioFilePath((FilePath)FixtureName).ReadByteStream(1000);
        var converter = new OggOpusStreamConverter();

        // act
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        var frames = await audio.GetFrames(CancellationToken.None).ToListAsync();

        // assert
        audio.Format.PreSkip.Should().Be(FixturePreSkip);
        audio.Format.CodecSettings.Should().Be(AudioSource.DefaultFormat.CodecSettings);
        frames.Should().HaveCount(FixtureFrameCount);
        frames[^1].Offset.Should().Be(Constants.Audio.OpusFrameDuration * (FixtureFrameCount - 1));
    }

    [Fact]
    public async Task AudioSourceShouldSniffTheOggContainer()
    {
        // arrange
        var byteStream = GetAudioFilePath((FilePath)FixtureName).ReadByteStream(1024);

        // act
        var audio = await AudioSource.ReadFromByteStream(byteStream, MomentClockSet.Default, Log, CancellationToken.None);
        var frames = await audio.GetFrames(CancellationToken.None).ToListAsync();

        // assert
        frames.Should().HaveCount(FixtureFrameCount);
        audio.Format.PreSkip.Should().Be(FixturePreSkip);
    }

    [Fact]
    public void ReaderShouldRejectAPageWithABadChecksum()
    {
        // arrange
        var bytes = ReadFixture();
        bytes[300] ^= 0xFF; // inside the third page's body
        var reader = new OggOpusReader();

        // act
        var act = () => {
            reader.Append(bytes);
            ReadAll(reader);
        };

        // assert
        act.Should().Throw<Exception>().WithMessage("*checksum*");
    }

    [Fact]
    public void ReaderShouldRejectAPacketThatIsNotOne20MsFrame()
    {
        // arrange: a 40 ms SILK packet (config 2 = SILK NB 40 ms, code 0) in place of the first audio packet
        var frames = new List<AudioFrame> {
            new() { Data = new byte[] { 0b00010_0_00, 1, 2, 3 }, Offset = TimeSpan.Zero },
        };
        var bytes = OggOpusTestStream.Write(frames);
        var reader = new OggOpusReader();

        // act
        var act = () => {
            reader.Append(bytes);
            ReadAll(reader);
        };

        // assert
        act.Should().Throw<Exception>().WithMessage("*40*ms*");
    }

    [Theory]
    [InlineData(0b00000_0_00, 1, 10)] // SILK NB 10 ms, 1 frame
    [InlineData(0b00001_0_00, 1, 20)] // SILK NB 20 ms
    [InlineData(0b00011_0_00, 1, 60)] // SILK NB 60 ms
    [InlineData(0b01100_0_00, 1, 10)] // Hybrid SWB 10 ms
    [InlineData(0b01101_0_00, 1, 20)] // Hybrid SWB 20 ms
    [InlineData(0b10000_0_00, 1, 2.5)] // CELT NB 2.5 ms
    [InlineData(0b11111_0_00, 1, 20)] // CELT FB 20 ms (what Soniox sends)
    [InlineData(0b11111_0_01, 1, 40)] // CELT FB 20 ms, code 1 = two frames
    [InlineData(0b11111_0_10, 1, 40)] // CELT FB 20 ms, code 2 = two frames
    [InlineData(0b11111_0_11, 3, 60)] // CELT FB 20 ms, code 3 = M frames
    public void PacketDurationShouldFollowTheToc(int toc, int frameCount, double expectedMs)
    {
        // arrange
        var packet = new byte[] { (byte)toc, (byte)frameCount, 0 };

        // act
        var duration = OggOpusReader.GetPacketDuration(packet);

        // assert
        duration.Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }

    [Fact]
    public void ReaderShouldStartOverOnANewLogicalStream()
    {
        // arrange: two complete Ogg/Opus streams back to back, as a multi-part REST response concatenates them
        var first = OggOpusTestStream.Write(5);
        var second = OggOpusTestStream.Write(3);
        var reader = new OggOpusReader();

        // act
        reader.Append(first);
        reader.Append(second);
        var frames = ReadAll(reader);

        // assert
        frames.Should().HaveCount(8, "the second stream's headers are skipped and its frames follow the first's");
        for (var i = 0; i < frames.Count; i++)
            frames[i].Offset.Should().Be(Constants.Audio.OpusFrameDuration * i);
    }

    // Private methods

    private static List<AudioFrame> ReadAll(OggOpusReader reader)
    {
        var frames = new List<AudioFrame>();
        while (reader.TryRead(out var frame))
            frames.Add(frame);
        return frames;
    }

    private static async IAsyncEnumerable<byte[]> Chunk(byte[] bytes, int chunkSize)
    {
        for (var i = 0; i < bytes.Length; i += chunkSize) {
            await Task.Yield();
            yield return bytes[i..Math.Min(bytes.Length, i + chunkSize)];
        }
    }

    private static byte[] ReadFixture()
        => File.ReadAllBytes(GetAudioFilePath((FilePath)FixtureName));

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    private AudioSource NewSyntheticSource(int frameCount, int preSkip)
        => new(
            MomentClockSet.Default.SystemClock.Now,
            AudioSource.DefaultFormat with { PreSkip = preSkip },
            OggOpusTestStream.Frames(frameCount).ToAsyncEnumerable(),
            TimeSpan.Zero,
            Log,
            CancellationToken.None);
}
