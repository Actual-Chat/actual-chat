using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Streaming;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Testing;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Streaming
{
    public class RecordingTest : TestBase
    {
        public RecordingTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async void EmptyRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var streamingService = services.GetRequiredService<IAudioStreamingService>();
            var serverSideStreamingService = services.GetRequiredService<IServerSideAudioStreamingService>();
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });
            var audioConfig = new AudioRecordingConfiguration(
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            
            var recordingTask = serverSideStreamingService.WaitForNewRecording(CancellationToken.None);
            
            _ = streamingService.UploadRecording(audioConfig, channel.Reader, CancellationToken.None);
            channel.Writer.Complete();

            var recording = await recordingTask; 
            recording!.Configuration.Should().Be(audioConfig);
            var channelReader = await serverSideStreamingService.GetRecording(recording.Id, CancellationToken.None);
            await foreach (var _ in channelReader.ReadAllAsync()) {}
        }
        
        [Fact]
        public async Task StreamRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var streamingService = services.GetRequiredService<IAudioStreamingService>(); 
            var serverSideStreamingService = services.GetRequiredService<IServerSideAudioStreamingService>();
            
            var recordingTask = serverSideStreamingService.WaitForNewRecording(CancellationToken.None);

            var writtenSize =  await PushRecording(streamingService);
            
            var recording = await recordingTask;
            var recordingStream = await serverSideStreamingService.GetRecording(recording!.Id, CancellationToken.None);
            var readSize = await recordingStream.ReadAllAsync().SumAsync(message => message.Chunk.Length);
            
            readSize.Should().Be(writtenSize);
        }
        
                
        [Fact]
        public async Task StreamTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var streamingService = services.GetRequiredService<IAudioStreamingService>(); 
            var serverSideStreamingService = services.GetRequiredService<IServerSideAudioStreamingService>();

            var streamId = (StreamId)"test-stream-id";
            var channel = Channel.CreateBounded<AudioMessage>(
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
        
        private static async Task<int> ReadDistributedData(StreamId streamId, IStreamingService<AudioMessage> sr)
        {
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
        
            return await audioReader.ReadAllAsync().SumAsync(message => message.Chunk.Length);
        }
        
        private static async Task<int> PushRecording(IAudioStreamingService streamingService)
        {
            var audioConfig = new AudioRecordingConfiguration(
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var readTask = ReadFromFile(channel.Writer);

            _ = streamingService.UploadRecording(audioConfig, channel.Reader, CancellationToken.None);

            return await readTask;
        }

        private static async Task<int> ReadFromFile(ChannelWriter<AudioMessage> writer)
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
                var command = new AudioMessage(
                    index++,
                    CpuClock.Now.EpochOffset.TotalSeconds,
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