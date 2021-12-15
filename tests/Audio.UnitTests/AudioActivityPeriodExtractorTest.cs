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

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services);
        var openAudioSegments = audioSplitter.GetSegments(record, blobStream, default);
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

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services);
        var openAudioSegments = audioSplitter.GetSegments(record, blobStream, default);
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

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services);
        var openAudioSegments = audioSplitter.GetSegments(record, blobStream, default);
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
    public async Task SplitStreamToMultipleSegmentsTest()
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
        var blobStreamWithBoundaries = InsertSpeechBoundaries(blobStream);
        var services = new ServiceCollection().BuildServiceProvider();
        var audioActivityExtractor = new AudioSplitter(services);
        var openAudioSegments = audioActivityExtractor.GetSegments(record, blobStreamWithBoundaries, default);
        var size = 0L;
        var index = 0;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(index++);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.ToBlobPart().Data.Length;
            var sum = 0;
            var offset = TimeSpan.Zero;
            await foreach (var f in audio.GetFrames(default)) {
                sum += f.Data.Length;

                (f.Offset - offset).Should().BeLessThan(TimeSpan.FromMilliseconds(80));
                offset = f.Offset;
            }
            size += sum;

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().Be(fileSize + 161); // + format of next segment

        async IAsyncEnumerable<BlobPart> InsertSpeechBoundaries(IAsyncEnumerable<BlobPart> bs)
        {
            var increment = 0;
            await foreach (var blobPart in bs) {
                if (blobPart.Index == 20) {
                    var nextBlockStartsAt = blobPart.Data
                        .Select((b, i) => (b == 0xA3 && blobPart.Data[i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new BlobPart(blobPart.Index + increment++, blobPart.Data[..nextBlockStartsAt]);
                    yield return new BlobPart(blobPart.Index + increment++, new byte[]{0, 0, 0, 0});

                    await Task.Delay(2 * 1000);

                    yield return new BlobPart(blobPart.Index + increment++, new byte[]{1, 1, 1, 1});
                    yield return new BlobPart(blobPart.Index + increment++, blobPart.Data[nextBlockStartsAt..]);
                }
                else
                    yield return new BlobPart(blobPart.Index + increment, blobPart.Data);
            }
        }
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName).ReadBlobStream(1024, cancellationToken);
        var audio = new AudioSource(blobStream, default, null, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
