using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Chat;
using ActualChat.Streaming;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Testing;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioOrchestratorTest : TestBase
    {
        public AudioOrchestratorTest(ITestOutputHelper @out) : base(@out)
            => AudioOrchestrator.SkipAutoStart = true;

        [Fact]
        public async Task NoRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var cts = new CancellationTokenSource();
            var dequeueTask = orchestrator.DequeueNewAudioRecord(cts.Token);
            await Task.Delay(50);
            dequeueTask.IsCompleted.Should().Be(false);
            cts.Cancel();
            await Task.Delay(50);
            dequeueTask.Status.Should().Be(TaskStatus.Canceled);
        }

        [Fact]
        public async Task EmptyRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var session = sessionFactory.CreateSession();
            _ = await appHost.SignIn(session, new User("", "Bob"));
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var audioUploader = services.GetRequiredService<IAudioRecorder>();
            var cts = new CancellationTokenSource();
            var dequeueTask = orchestrator.DequeueNewAudioRecord(cts.Token);

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
            _ = audioUploader.Record(session,recordingSpec, channel.Reader, CancellationToken.None);
            channel.Writer.Complete();

            var record = await dequeueTask;
            record.Should().Be(recordingSpec with {
                Id = record!.Id,
                UserId = record.UserId
            });
        }

        [Fact]
        public async Task PerformRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            var services = appHost.Services;
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var session = sessionFactory.CreateSession();
            _ = await appHost.SignIn(session, new User("", "Bob"));
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var audioUploader = services.GetRequiredService<IAudioRecorder>();
            var blobStreamer = services.GetRequiredService<IAudioStreamReader>();
            var chatService = services.GetRequiredService<IChatService>();

            var chat = await chatService.Create(new ChatCommands.Create(session, "Test"), default);
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.DequeueNewAudioRecord(cts.Token);

            var pushAudioTask = PushAudioData(session, chat.Id, audioUploader);

            var recording = await recordingTask;
            var pipelineTask = orchestrator.StartAudioPipeline(recording!, cts.Token);

            var readTask = ReadDistributedData(recording!.Id, blobStreamer);
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
            var sessionFactory = services.GetRequiredService<ISessionFactory>();
            var session = sessionFactory.CreateSession();
            _ = await appHost.SignIn(session, new User("", "Bob"));
            var orchestrator = services.GetRequiredService<AudioOrchestrator>();
            var streamingService = services.GetRequiredService<IAudioStreamReader>();
            var audioUploader = services.GetRequiredService<IAudioRecorder>();
            var transcriptStreamer = services.GetRequiredService<ITranscriptStreamReader>();
            var chatService = services.GetRequiredService<IChatService>();

            var chat = await chatService.Create(new ChatCommands.Create(session, "Test"), default);
            var cts = new CancellationTokenSource();
            var recordingTask = orchestrator.DequeueNewAudioRecord(cts.Token);

            var pushAudioTask = PushAudioData(session, chat.Id, audioUploader);

            var recording = await recordingTask;
            var pipelineTask = orchestrator.StartAudioPipeline(recording!, cts.Token);

            var readTask = ReadDistributedData(recording!.Id, streamingService);
            var readTranscriptTask = ReadTranscribedData(recording!.Id, transcriptStreamer);
            var writtenSize = await pushAudioTask;
            var readSize = await readTask;
            var transcribed = await readTranscriptTask;

            transcribed.Should().BeGreaterThan(0);

            await pipelineTask;

            readSize.Should().Be(writtenSize);
        }

        private async Task<int> ReadTranscribedData(AudioRecordId audioRecordId, IStreamReader<TranscriptPart> sr)
        {
            var size = 0;
            //TODO: AK - we need to figure out how to notify consumers about new streamID - with new ChatEntry?
            var streamId = new StreamId(audioRecordId, 0);
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
            await foreach (var message in audioReader.ReadAllAsync()) {
                Out.WriteLine(message.Text);
                size = message.TextOffset + message.Text.Length;
            }

            return size;
        }

        private static async Task<int> ReadDistributedData(AudioRecordId audioRecordId, IStreamReader<BlobPart> blobStreamReader)
        {
            var streamId = new StreamId(audioRecordId, 0);
            var audioReader = await blobStreamReader.GetStream(streamId, CancellationToken.None);

            return await audioReader.ReadAllAsync().SumAsync(message => message.Chunk.Length);
        }

        private static async Task<int> PushAudioData(Session session, string chatId, IAudioRecorder audioRecorder)
        {
            var record = new AudioRecord(
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

            _ = audioRecorder.Record(session, record, channel.Reader, CancellationToken.None);

            var size = 0;
            await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"), FileMode.Open, FileAccess.Read);
            using var readBufferLease = MemoryPool<byte>.Shared.Rent(1 * 1024);
            var readBuffer = readBufferLease.Memory;
            var index = 0;
            var bytesRead = await inputStream.ReadAsync(readBuffer);
            size += bytesRead;
            while (bytesRead > 0) {
                var command = new BlobPart(
                    index++,
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
