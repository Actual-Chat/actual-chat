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
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var size = 0;
            var segments = audioActivityExtractor.GetSegmentsWithAudioActivity(record, channel, default);
            await foreach (var segment in segments.ReadAllAsync()) {
                segment.Index.Should().Be(0);
                segment.AudioRecord.Should().Be(record);
                var audio = await segment.GetAudioStream();
                size += await audio.ReadAllAsync().SumAsync(audioMessage => audioMessage.Data.Length);

                var part = await segment.GetAudioStreamPart();
                part.Document.Should().NotBeNull();
                part.Metadata.Count.Should().BeGreaterThan(0);
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
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var segments = audioActivityExtractor.GetSegmentsWithAudioActivity(record, channel, default);
            await foreach (var segment in segments.ReadAllAsync()) {
                segment.Index.Should().Be(0);
                segment.AudioRecord.Should().Be(record);
                var audio = await segment.GetAudioStream();
                size += await audio
                    .ReadAllAsync()
                    .SumAsync(p => p.Data.Length);

                var part = await segment.GetAudioStreamPart();
                part.Document.Should().NotBeNull();
                part.Metadata.Count.Should().BeGreaterThan(0);
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
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var segments = audioActivityExtractor.GetSegmentsWithAudioActivity(record, channel, default);
            await foreach (var segment in segments.ReadAllAsync()) {
                segment.Index.Should().Be(0);
                segment.AudioRecord.Should().Be(record);

                var part = await segment.GetAudioStreamPart();
                part.Document.Should().NotBeNull();
                part.Metadata.Count.Should().BeGreaterThan(0);
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
