using System.Buffers;
using System.Threading.Channels;
using ActualChat.Audio.Processing;
using ActualChat.Blobs;
using Stl.Testing;
using Stl.Time;
using Xunit.Abstractions;

namespace ActualChat.Audio.UnitTests
{
    public class AudioActivityPeriodExtractorTest : TestBase
    {
        public AudioActivityPeriodExtractorTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async Task SplitStreamReadBeforeCompletionTest()
        {
            var audioActivityExtractor = new AudioActivityExtractor();
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var readTask = ReadFromFile(channel.Writer);
            var record = new AudioRecord(
                "test-id", "1", "1",
                new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var size = 0;
            var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, channel, default);
            await foreach (var openAudioSegment in openAudioSegments.ReadAllAsync()) {
                openAudioSegment.Index.Should().Be(0);
                openAudioSegment.AudioRecord.Should().Be(record);
                var audio = openAudioSegment.Source;
                var header = Convert.FromBase64String(audio.Format.CodecSettings);

                size += header.Length;
                size += await audio.SumAsync(audioMessage => audioMessage.Data.Length);

                var audioSegment = await openAudioSegment.Close();
                audioSegment.AudioSource.Should().NotBeNull();
                audioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            }

            var bytesRead = await readTask;
            size.Should().Be(bytesRead);
        }

        [Fact]
        public async Task SplitStreamReadAfterCompletionTest()
        {
            var audioActivityExtractor = new AudioActivityExtractor();
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var size = 0;
            var readTask = ReadFromFile(channel.Writer);
            var record = new AudioRecord(
                "test-id", "1", "1",
                new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, channel, default);
            await foreach (var openAudioSegment in openAudioSegments.ReadAllAsync()) {
                openAudioSegment.Index.Should().Be(0);
                openAudioSegment.AudioRecord.Should().Be(record);
                var audio = openAudioSegment.Source;
                var header = Convert.FromBase64String(audio.Format.CodecSettings);

                size += header.Length;
                size += await audio.SumAsync(p => p.Data.Length);

                var audioSegment = await openAudioSegment.Close();
                audioSegment.AudioSource.Should().NotBeNull();
                audioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            }

            var bytesRead = await readTask;
            size.Should().Be(bytesRead);
        }

        [Fact]
        public async Task SplitStreamDontReadTest()
        {
            var audioActivityExtractor = new AudioActivityExtractor();
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = ReadFromFile(channel);
            var record = new AudioRecord(
                "test-id", "1", "1",
                new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var openAudioSegments = audioActivityExtractor.SplitToAudioSegments(record, channel, default);
            await foreach (var openAudioSegment in openAudioSegments.ReadAllAsync()) {
                openAudioSegment.Index.Should().Be(0);
                openAudioSegment.AudioRecord.Should().Be(record);

                var audioSegment = await openAudioSegment.Close();
                audioSegment.AudioSource.Should().NotBeNull();
                audioSegment.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            }
        }

        private static async Task<int> ReadFromFile(ChannelWriter<BlobPart> writer)
        {
            var size = 0;
            await using var inputStream = new FileStream(
                Path.Combine(Environment.CurrentDirectory, @"data", "file.webm"),
                FileMode.Open,
                FileAccess.Read);
            using var readBufferLease = MemoryPool<byte>.Shared.Rent(1 * 1024);
            var readBuffer = readBufferLease.Memory;
            var index = 0;
            var bytesRead = await inputStream.ReadAsync(readBuffer);
            size += bytesRead;
            while (bytesRead > 0) {
                var command = new BlobPart(
                    index++,
                    readBuffer[..bytesRead].ToArray());
                await writer.WriteAsync(command, CancellationToken.None);

                // await Task.Delay(300); //emulate real-time speech delay
                bytesRead = await inputStream.ReadAsync(readBuffer);
                size += bytesRead;
            }

            writer.Complete();
            return size;
        }
    }
}
