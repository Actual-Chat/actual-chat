using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Streaming;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Testing;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Streaming
{
    public class RecordingTest : TestBase
    {
        public RecordingTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public async void EmptyRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var session = sessionFactory.CreateSession();
            _ = await appHost.SignIn(session, new User("", "Bob"));
            var audioRecorder = services.GetRequiredService<IAudioRecorder>();
            var audioRecordReader = services.GetRequiredService<AudioRecordReader>();
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });
            var recordingSpec = new AudioRecord(
                "1",
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);

            var recordingTask = audioRecordReader.DequeueNewRecord(CancellationToken.None);

            _ = audioRecorder.Record(session, recordingSpec, channel.Reader, CancellationToken.None);
            channel.Writer.Complete();

            var recording = await recordingTask;
            recording.Should().Be(recordingSpec with {
                Id = recording!.Id,
                UserId = recording.UserId
            });

            var channelReader = await audioRecordReader.GetContent(recording.Id, CancellationToken.None);
            await foreach (var _ in channelReader.ReadAllAsync()) {}
        }

        [Fact]
        public async Task StreamRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var session = sessionFactory.CreateSession();
            _ = await appHost.SignIn(session, new User("", "Bob"));
            var audioRecorder = services.GetRequiredService<IAudioRecorder>();
            var audioRecordReader = services.GetRequiredService<AudioRecordReader>();

            var dequeueTask = audioRecordReader.DequeueNewRecord(CancellationToken.None);
            var writtenSize =  await UploadRecording(session, "1", audioRecorder);

            var record = await dequeueTask;
            var recordStream = await audioRecordReader.GetContent(record!.Id, CancellationToken.None);
            var readSize = await recordStream.ReadAllAsync().SumAsync(message => message.Chunk.Length);

            readSize.Should().Be(writtenSize);
        }


        [Fact]
        public async Task StreamTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var streamingService = services.GetRequiredService<IStreamReader<BlobPart>>();
            var serverSideStreamingService = services.GetRequiredService<IStreamPublisher<BlobPart>>();

            var streamId = (StreamId)"test-stream-id";
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var writeTask = ReadFromFile(channel.Writer);

            var publishTask =  serverSideStreamingService.PublishStream(streamId, channel.Reader, CancellationToken.None);
            var readTask = ReadDistributedData(streamId, streamingService);

            await Task.WhenAll(writeTask, readTask);
            await publishTask;

            var writtenSize = await writeTask;
            var readSize = await readTask;

            readSize.Should().Be(writtenSize);
        }

        private static async Task<int> ReadDistributedData(StreamId streamId, IStreamReader<BlobPart> sr)
        {
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
            return await audioReader.ReadAllAsync().SumAsync(message => message.Chunk.Length);
        }

        private static async Task<int> UploadRecording(Session session, string chatId, IAudioRecorder audioRecorder)
        {
            var recording = new AudioRecord(
                chatId,
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var readTask = ReadFromFile(channel.Writer);
            var uploadTask = audioRecorder.Record(session, recording, channel.Reader, CancellationToken.None);
            await Task.WhenAll(readTask, uploadTask);
            return await readTask;
        }

        private static async Task<int> ReadFromFile(ChannelWriter<BlobPart> writer)
        {
            var size = 0;
            await using var inputStream = new FileStream(
                Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
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
