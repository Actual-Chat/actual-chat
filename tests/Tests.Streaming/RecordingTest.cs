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

namespace ActualChat.Tests
{
    public class RecordingTest : TestBase
    {
        public RecordingTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async void EmptyAudioRecordingTest()
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
            var cts = new CancellationTokenSource();
            
            var recordingTask = serverSideStreamingService.WaitForNewRecording(cts.Token);

            var pushAudioTask = PushAudioData(streamingService);
            
            var recording = await recordingTask;
            var recordingStreamTask = serverSideStreamingService.GetRecording(recording!.Id, cts.Token);
            
            var readTask = ReadDistributedData(recording!.Id, streamingService);
            var writtenSize = await pushAudioTask;
            var readSize = await readTask;

            await recordingStreamTask;
            
            readSize.Should().Be(writtenSize);
        }
        
        private static async Task<int> ReadDistributedData(RecordingId recordingId, IStreamingService<AudioMessage> sr)
        {
            var streamId = $"{recordingId}-{0:D4}";
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);

            return await audioReader.ReadAllAsync().SumAsync(message => message.Chunk.Length);
        }
        
        private static async Task<int> PushAudioData(IAudioStreamingService streamingService)
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

            _ = streamingService.UploadRecording(audioConfig, channel.Reader, CancellationToken.None);
            
            var size = 0;
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
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
                await channel.Writer.WriteAsync(command, CancellationToken.None);

                // await Task.Delay(300); //emulate real-time speech delay
                bytesRead = await inputStream.ReadAsync(readBuffer);
                size += bytesRead;
            }

            channel.Writer.Complete();
            return size;
        }
    }
}