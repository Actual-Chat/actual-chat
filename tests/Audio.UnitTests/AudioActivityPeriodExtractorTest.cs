using ActualChat.Audio.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class AudioActivityPeriodExtractorTest : TestBase
{
    public AudioActivityPeriodExtractorTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task SplitStreamDontReadTest()
    {
        var record = new AudioRecord(
            "test-id",
            "1",
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        var blobStream = GetAudioFilePath("file.webm").ReadBlobStream();

        var audioActivityExtractor = new AudioActivityExtractor(NullLoggerFactory.Instance);
        var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, blobStream);
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(0);
            openAudioSegment.AudioRecord.Should().Be(record);

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    [Fact]
    public async Task SplitStreamReadAfterCompletionTest()
    {
        var record = new AudioRecord(
            "test-id",
            "1",
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var blobStream = audioFilePath.ReadBlobStream();

        var audioActivityExtractor = new AudioActivityExtractor(NullLoggerFactory.Instance);
        var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, blobStream);
        var size = 0L;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(0);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.ToBlobPart().Data.Length;
            size += await audio.GetFrames(default).SumAsync(f => f.Data.Length);

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().Be(fileSize);
    }

    [Fact]
    public async Task SplitStreamReadBeforeCompletionTest()
    {
        var record = new AudioRecord(
            "test-id",
            "1",
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var blobStream = audioFilePath.ReadBlobStream();

        var audioActivityExtractor = new AudioActivityExtractor(NullLoggerFactory.Instance);
        var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, blobStream);
        var size = 0L;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(0);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.ToBlobPart().Data.Length;
            size += await audio.GetFrames(default).SumAsync(f => f.Data.Length);

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().Be(fileSize);
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName).ReadBlobStream(cancellationToken);
        var audio = new AudioSource(blobStream, default, null, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
