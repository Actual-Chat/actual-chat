using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Audio;
using ActualChat.Audio.Orchestration;
using ActualChat.Streaming;
using FluentAssertions;
using Stl.Testing;
using Stl.Time;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Audio
{
    public class AudioStreamSplitterTest : TestBase
    {
        public AudioStreamSplitterTest(ITestOutputHelper @out) : base(@out)
        {
            
        }

        [Fact]
        public async Task SplitStreamReadBeforeCompletionTest()
        {
            var splitter = new AudioStreamSplitter();
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var readTask = ReadFromFile(channel.Writer);
            var audioConfig = new AudioRecordingConfiguration(
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            var audioRecording = new AudioRecording("test-id", audioConfig);

            var size = 0;
            var streamEntries = splitter.SplitBySilencePeriods(audioRecording, channel.Reader, CancellationToken.None);
            await foreach (var audioStreamEntry in streamEntries) {
                audioStreamEntry.Index.Should().Be(0);
                audioStreamEntry.AudioRecording.Should().Be(audioRecording);
                size += await audioStreamEntry.GetStream().ReadAllAsync().SumAsync(audioMessage => audioMessage.Chunk.Length);
                var entry = await audioStreamEntry.GetEntryOnCompletion(CancellationToken.None);

                entry.Document.Should().NotBeNull();
                entry.MetaData.Count.Should().BeGreaterThan(0);
            }

            var bytesRead = await readTask;
            size.Should().Be(bytesRead);
        } 
        
        [Fact]
        public async Task SplitStreamReadAfterCompletionTest()
        {
            var splitter = new AudioStreamSplitter();
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            var size = 0;
            var readTask = ReadFromFile(channel.Writer);
            var audioConfig = new AudioRecordingConfiguration(
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            var audioRecording = new AudioRecording("test-id", audioConfig);

            var streamEntries = splitter.SplitBySilencePeriods(audioRecording, channel.Reader, CancellationToken.None);
            await foreach (var audioStreamEntry in streamEntries) {
                audioStreamEntry.Index.Should().Be(0);
                audioStreamEntry.AudioRecording.Should().Be(audioRecording);
                var entry = await audioStreamEntry.GetEntryOnCompletion(CancellationToken.None);
                size += await audioStreamEntry.GetStream().ReadAllAsync().SumAsync(audioMessage => audioMessage.Chunk.Length);

                entry.Document.Should().NotBeNull();
                entry.MetaData.Count.Should().BeGreaterThan(0);
            }
            
            var bytesRead = await readTask;
            size.Should().Be(bytesRead);
        } 
        
        [Fact]
        public async Task SplitStreamDontReadTest()
        {
            var splitter = new AudioStreamSplitter();
            var channel = Channel.CreateBounded<AudioMessage>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var readTask = ReadFromFile(channel.Writer);
            var audioConfig = new AudioRecordingConfiguration(
                new AudioFormat { Codec = AudioCodec.Opus, ChannelCount = 1, SampleRate = 48_000 },
                "RU-ru",
                CpuClock.Now.EpochOffset.TotalSeconds);
            var audioRecording = new AudioRecording("test-id", audioConfig);

            var streamEntries = splitter.SplitBySilencePeriods(audioRecording, channel.Reader, CancellationToken.None);
            await foreach (var audioStreamEntry in streamEntries) {
                audioStreamEntry.Index.Should().Be(0);
                audioStreamEntry.AudioRecording.Should().Be(audioRecording);
                var entry = await audioStreamEntry.GetEntryOnCompletion(CancellationToken.None);

                entry.Document.Should().NotBeNull();
                entry.MetaData.Count.Should().BeGreaterThan(0);
            }
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