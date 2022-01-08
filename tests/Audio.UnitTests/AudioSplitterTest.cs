using ActualChat.Audio.Processing;
using ActualChat.Media;
using Stl.IO;

namespace ActualChat.Audio.UnitTests;

public class AudioSplitterTest : TestBase
{
    public AudioSplitterTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task SplitStreamDontReadTest()
    {
        var record = new AudioRecord(
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var byteStream = GetAudioFilePath("file.webm").ReadByteStream();

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services, true);
        var openAudioSegments = audioSplitter.GetSegments(record, null!, byteStream.ToRecordingStream(), default);
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
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var byteStream = audioFilePath.ReadByteStream();

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services, true);
        var openAudioSegments = audioSplitter.GetSegments(record, null!, byteStream.ToRecordingStream(), default);
        var size = 0L;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(0);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.Serialize().Length;
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
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var byteStream = audioFilePath.ReadByteStream();

        var services = new ServiceCollection().BuildServiceProvider();
        var audioSplitter = new AudioSplitter(services, true);
        var openAudioSegments = audioSplitter.GetSegments(record, null!, byteStream.ToRecordingStream(), default);
        var size = 0L;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(0);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.Serialize().Length;
            size += await audio.GetFrames(default).SumAsync(f => f.Data.Length);

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().Be(fileSize);
    }

    [Fact]
    public async Task SplitStreamToMultipleSegmentsUsingStopResumeRecordingTest()
    {
        var record = new AudioRecord(
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var byteStream = audioFilePath.ReadByteStream();
        var blobStreamWithBoundaries = InsertSpeechBoundaries(byteStream.ToRecordingStream());
        var services = new ServiceCollection().BuildServiceProvider();
        var audioActivityExtractor = new AudioSplitter(services, true);
        var openAudioSegments = audioActivityExtractor.GetSegments(record, null!, blobStreamWithBoundaries, default);
        var size = 0L;
        var index = 0;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(index++);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.Serialize().Length;
            var sum = 0;
            var offset = TimeSpan.Zero;
            var frameIndex = 0;
            await foreach (var f in audio.GetFrames(default)) {
                sum += f.Data.Length;
                frameIndex++;
                (f.Offset - offset).Should().BeLessThan(TimeSpan.FromMilliseconds(80));
                if (offset > TimeSpan.Zero)
                    (f.Offset - offset).Should().BeGreaterThan(TimeSpan.FromMilliseconds(19));
                offset = f.Offset;
            }
            size += sum;

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().Be(fileSize + 161); // + format of next segment

        async IAsyncEnumerable<RecordingPart> InsertSpeechBoundaries(IAsyncEnumerable<RecordingPart> rs)
        {
            yield return new RecordingPart(RecordingEventKind.Resume) {
                RecordedAt = SystemClock.Now,
                Offset = TimeSpan.FromSeconds(0),
            };

            var i = 0;
            await foreach (var recordingPart in rs) {
                if (i++ == 20) {
                    var nextBlockStartsAt = recordingPart.Data
                        .Select((b, i) => (b == 0xA3 && recordingPart.Data![i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[..nextBlockStartsAt]};
                    yield return new RecordingPart(RecordingEventKind.Pause) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10),
                    };

                    yield return new RecordingPart(RecordingEventKind.Resume) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10),
                    };

                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[nextBlockStartsAt..]};
                }
                else
                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data };
            }
        }
    }

    [Fact]
    public async Task SplitStreamToMultipleSegmentsUsingStopResumeSendingTest()
    {
        var record = new AudioRecord(
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var byteStream = audioFilePath.ReadByteStream();
        var blobStreamWithBoundaries = InsertSpeechBoundaries(byteStream.ToRecordingStream());
        var services = new ServiceCollection().BuildServiceProvider();
        var audioActivityExtractor = new AudioSplitter(services, true);
        var openAudioSegments = audioActivityExtractor.GetSegments(record, null!, blobStreamWithBoundaries, default);
        var size = 0L;
        var index = 0;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(index++);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.Serialize().Length;
            var sum = 0;
            var offset = TimeSpan.Zero;
            await foreach (var f in audio.GetFrames(default)) {
                sum += f.Data.Length;

                (f.Offset - offset).Should().BeLessThan(TimeSpan.FromMilliseconds(80));
                if (offset > TimeSpan.Zero)
                    (f.Offset - offset).Should().BeGreaterThan(TimeSpan.FromMilliseconds(19));
                offset = f.Offset;
            }
            size += sum;

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().BeLessThan(fileSize + 161 - (3 * 300)); // + format of next segment


        async IAsyncEnumerable<RecordingPart> InsertSpeechBoundaries(IAsyncEnumerable<RecordingPart> rs)
        {
            yield return new RecordingPart(RecordingEventKind.Resume) {
                RecordedAt = SystemClock.Now,
                Offset = TimeSpan.FromSeconds(0),
            };

            var i = 0;
            await foreach (var recordingPart in rs) {
                i++;
                if (i == 20) {
                    var nextBlockStartsAt = recordingPart.Data
                        .Select((b, i) => (b == 0xA3 && recordingPart.Data![i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[..nextBlockStartsAt]};
                    yield return new RecordingPart(RecordingEventKind.Pause) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10),
                    };

                }
                else if (i is > 20 and <= 23) {}
                else if (i == 24) {
                    var nextBlockStartsAt = recordingPart.Data
                        .Select((b, i) => (b == 0xA3 && recordingPart.Data![i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new RecordingPart(RecordingEventKind.Resume) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10),
                    };
                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[nextBlockStartsAt..]};

                }
                else
                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data };
            }
        }
    }


    [Fact]
    public async Task SkipGapTest()
    {
        var record = new AudioRecord(
            "test-id", "", "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var audioFilePath = GetAudioFilePath("file.webm");
        var fileSize = audioFilePath.GetFileInfo().Length;
        var byteStream = audioFilePath.ReadByteStream();
        var blobStreamWithBoundaries = InsertSpeechBoundaries(byteStream.ToRecordingStream());
        var services = new ServiceCollection().BuildServiceProvider();
        var audioActivityExtractor = new AudioSplitter(services, true);
        var openAudioSegments = audioActivityExtractor.GetSegments(record, null!, blobStreamWithBoundaries, default);
        var size = 0L;
        var index = 0;
        await foreach (var openAudioSegment in openAudioSegments) {
            openAudioSegment.Index.Should().Be(index++);
            openAudioSegment.AudioRecord.Should().Be(record);
            var audio = openAudioSegment.Audio;
            await audio.WhenFormatAvailable;

            size += audio.Format.Serialize().Length;
            var sum = 0;
            var offset = TimeSpan.Zero;
            await foreach (var f in audio.GetFrames(default)) {
                sum += f.Data.Length;

                (f.Offset - offset).Should().BeLessThan(TimeSpan.FromMilliseconds(80));
                if (offset > TimeSpan.Zero)
                    (f.Offset - offset).Should().BeGreaterThan(TimeSpan.FromMilliseconds(19));
                offset = f.Offset;
            }
            size += sum;

            var closedAudioSegment = await openAudioSegment.ClosedSegmentTask;
            closedAudioSegment.Audio.Should().NotBeNull();
            closedAudioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        size.Should().BeLessThan(fileSize - (3 * 300)); // + format of next segment

        async IAsyncEnumerable<RecordingPart> InsertSpeechBoundaries(IAsyncEnumerable<RecordingPart> rs)
        {
            yield return new RecordingPart(RecordingEventKind.Resume) {
                RecordedAt = SystemClock.Now,
                Offset = TimeSpan.FromSeconds(0),
            };

            var i = 0;
            await foreach (var recordingPart in rs) {
                i++;
                if (i == 20) {
                    var nextBlockStartsAt = recordingPart.Data
                        .Select((b, i) => (b == 0xA3 && recordingPart.Data![i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[..nextBlockStartsAt]};
                    yield return new RecordingPart(RecordingEventKind.Pause) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10),
                    };

                }
                else if (i is > 20 and <= 23) {}
                else if (i == 24) {
                    var nextBlockStartsAt = recordingPart.Data
                        .Select((b, i) => (b == 0xA3 && recordingPart.Data![i + 3] == 0x81, i))
                        .First(x => x.Item1).i;

                    yield return new RecordingPart(RecordingEventKind.Resume) {
                        RecordedAt = SystemClock.Now,
                        Offset = TimeSpan.FromSeconds(10.2),
                    };
                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data[nextBlockStartsAt..]};

                }
                else
                    yield return new RecordingPart(RecordingEventKind.Data) { Data = recordingPart.Data };
            }
        }
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, CancellationToken cancellationToken = default)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, cancellationToken);
        var audio = new AudioSource(byteStream, default, null, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
