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
using Stl.Text;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioOrchestratorTest : TestBase
    {
        public AudioOrchestratorTest(ITestOutputHelper @out) : base(@out)
        {
            AudioOrchestrator.SkipAutoStart = true;
        }

        [Fact]
        public async Task NoRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.WaitForNewRecording(cts.Token);
            await Task.Delay(50);
            recordingTask.IsCompleted.Should().Be(false);
            cts.Cancel();
            await Task.Delay(50);
            recordingTask.Status.Should().Be(TaskStatus.Canceled);
        }
        
        [Fact]
        public async Task EmptyRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var streamingService = services.GetRequiredService<IAudioStreamingService>(); 
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.WaitForNewRecording(cts.Token);
            
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
            _ = streamingService.UploadRecording(audioConfig, channel.Reader, CancellationToken.None);
            channel.Writer.Complete();
            
            var recording = await recordingTask;
            recording.Should().NotBeNull();
            recording!.Configuration.Should().Be(audioConfig);
        }
        
        [Fact]
        public async Task PerformRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var streamingService = services.GetRequiredService<IAudioStreamingService>(); 
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.WaitForNewRecording(cts.Token);

            var pushAudioTask = PushAudioData(streamingService);
            
            var recording = await recordingTask;
            var pipelineTask = orchestrator.StartAudioPipeline(recording!, cts.Token);
            
            var readTask = ReadDistributedData(recording!.Id, streamingService);
            var writtenSize = await pushAudioTask;
            var readSize = await readTask;

            await pipelineTask;
            
            readSize.Should().Be(writtenSize);
        }
        
        [Fact]
        public async Task PerformRecordingAndTranscriptionTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var streamingService = services.GetRequiredService<IAudioStreamingService>(); 
            var transcriptStreaming = services.GetRequiredService<IStreamingService<TranscriptMessage>>();
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.WaitForNewRecording(cts.Token);

            var pushAudioTask = PushAudioData(streamingService);
            
            var recording = await recordingTask;
            var pipelineTask = orchestrator.StartAudioPipeline(recording!, cts.Token);
            
            var readTask = ReadDistributedData(recording!.Id, streamingService);
            var readTranscriptTask = ReadTranscribedData(recording!.Id, transcriptStreaming);
            var writtenSize = await pushAudioTask;
            var readSize = await readTask;
            var transcribed = await readTranscriptTask;

            transcribed.Should().BeGreaterThan(0);

            await pipelineTask;
            
            readSize.Should().Be(writtenSize);
        }
        
        private async Task<int> ReadTranscribedData(RecordingId recordingId, IStreamingService<TranscriptMessage> sr)
        {
            var size = 0;
            //TODO: AK - we need to figure out how to notify consumers about new streamID - with new ChatEntry?
            var streamId = new StreamId(recordingId, 0);
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
            await foreach (var message in audioReader.ReadAllAsync()) {
                Out.WriteLine(message.Text);
                size = message.TextIndex + message.Text.Length;
            }

            return size;
        }
        
        private static async Task<int> ReadDistributedData(RecordingId recordingId, IStreamingService<AudioMessage> sr)
        {
            var streamId = new StreamId(recordingId, 0);
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