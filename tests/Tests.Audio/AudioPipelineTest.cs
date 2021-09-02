using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Distribution;
using ActualChat.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Testing;
using Stl.Text;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioPipelineTest : TestBase
    {
        public AudioPipelineTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public async Task InitCompleteRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            _ = await blazorTester.SignIn(new User("", "Bob"));
            var services = appHost.Services;
            var session = blazorTester.Session;

            var audioRecorder = services.GetRequiredService<IAudioRecorder>();

            var initializeCommand = new InitializeAudioRecorderCommand {
                Session = session,
                Language = "RU-ru",
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                ClientStartOffset = CpuClock.Now
            };
            var initResult = await audioRecorder.Initialize(initializeCommand, CancellationToken.None);
            initResult.Value.Should().NotBeNullOrWhiteSpace();

            var recordingId = initResult;
            await audioRecorder.Complete(new CompleteAudioRecording(recordingId));
        }
        
        [Fact]
        public async Task PerformRecordingTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            _ = await blazorTester.SignIn(new User("", "Bob"));
            var services = appHost.Services;
            var session = blazorTester.Session;

            var audioRecorder = services.GetRequiredService<IAudioRecorder>();
            var audioStreaming = services.GetRequiredService<IAudioStreamingService>();


            var initializeCommand = new InitializeAudioRecorderCommand {
                Session = session,
                Language = "RU-ru",
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                ClientStartOffset = CpuClock.Now
            };
            var initResult = await audioRecorder.Initialize(initializeCommand, CancellationToken.None);
            initResult.Value.Should().NotBeNullOrWhiteSpace();
            var recordingId = initResult;

            var pushTask = PushAudioData(recordingId, audioStreaming);
            var readTask = ReadDistributedData(recordingId, audioStreaming);

            var writtenSize = await pushTask;
            await Task.Delay(1000); // Iron pants
            
            await audioRecorder.Complete(new CompleteAudioRecording(recordingId));
            var readSize = await readTask;

            readSize.Should().Be(writtenSize);
        }
        
        [Fact]
        public async Task PerformRecordingAndTranscriptionTest()
        {
            using var appHost = await TestHostFactory.NewAppHost();
            using var blazorTester = appHost.NewBlazorTester();
            _ = await blazorTester.SignIn(new User("", "Bob"));
            var services = appHost.Services;
            var session = blazorTester.Session;

            var audioRecorder = services.GetRequiredService<IAudioRecorder>();
            var audioStreaming = services.GetRequiredService<IAudioStreamingService>();
            var transcriptStreaming = services.GetRequiredService<IStreamingService<TranscriptMessage>>();

            var initializeCommand = new InitializeAudioRecorderCommand {
                Session = session,
                Language = "RU-ru",
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                ClientStartOffset = CpuClock.Now
            };
            var initResult = await audioRecorder.Initialize(initializeCommand, CancellationToken.None);
            initResult.Value.Should().NotBeNullOrWhiteSpace();
            var recordingId = initResult;

            var pushTask = PushAudioData(recordingId, audioStreaming);
            var readTask = ReadDistributedData(recordingId, audioStreaming);
            var readTranscriptTask = ReadTranscribedData(recordingId, transcriptStreaming);

            var writtenSize = await pushTask;
            // await Task.Delay(2000); // Iron pants
            
            await audioRecorder.Complete(new CompleteAudioRecording(recordingId));
            var readSize = await readTask;
            var transcribed = await readTranscriptTask;

            transcribed.Should().BeGreaterThan(0);

            readSize.Should().Be(writtenSize);
        }
        
        private static async Task<int> ReadDistributedData(Symbol recordingId, IStreamingService<AudioMessage> sr)
        {
            var size = 0;
            //TODO: AK - we need to figure out how to notify consumers about new streamID - with new ChatEntry?
            var streamId = $"{recordingId}-{0:D4}";
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
            await foreach (var message in audioReader.ReadAllAsync()) size += message.Chunk.Length;

            return size;
        }
        
        private async Task<int> ReadTranscribedData(Symbol recordingId, IStreamingService<TranscriptMessage> sr)
        {
            var size = 0;
            //TODO: AK - we need to figure out how to notify consumers about new streamID - with new ChatEntry?
            var streamId = $"{recordingId}-{0:D4}";
            var audioReader = await sr.GetStream(streamId, CancellationToken.None);
            await foreach (var message in audioReader.ReadAllAsync()) {
                Out.WriteLine(message.Text);
                size = message.TextIndex + message.Text.Length;
            }

            return size;
        }

        private static async Task<int> PushAudioData(Symbol recordingId, IAudioStreamingService streamingService)
        {
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            _ = streamingService.UploadStream(null, channel.Reader, CancellationToken.None);
            
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