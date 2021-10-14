using System.Buffers;
using ActualChat.Blobs;
using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Audio.IntegrationTests;

public class SourceAudioRecorderTest : AppHostTestBase
{
    public SourceAudioRecorderTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async void EmptyRecordingTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var sourceAudioRecorder = services.GetRequiredService<SourceAudioRecorder>();
        var channel = Channel.CreateBounded<BlobPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });
        var recordSpec = new AudioRecord(
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);

        var recordTask = sourceAudioRecorder.DequeueSourceAudio(CancellationToken.None);
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordSpec, channel.Reader, CancellationToken.None);
        channel.Writer.Complete();

        var record = await recordTask;
        record.Should().Be(recordSpec with {
            Id = record!.Id,
            UserId = record.UserId
        });

        var stream = sourceAudioRecorder.GetSourceAudioStream(record.Id, CancellationToken.None);
        await foreach (var _ in stream.ReadAllAsync()) {}
    }

    [Fact]
    public async Task StreamRecordingTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var sourceAudioRecorder = services.GetRequiredService<SourceAudioRecorder>();

        var recordTask = sourceAudioRecorder.DequeueSourceAudio(CancellationToken.None);
        var writtenSize =  await UploadRecording(session, "1", sourceAudioRecorder);

        var record = await recordTask;
        var recordStream = sourceAudioRecorder.GetSourceAudioStream(record!.Id, CancellationToken.None);
        var readSize = await recordStream.ReadAllAsync().SumAsync(message => message.Data.Length);

        readSize.Should().Be(writtenSize);
    }


    [Fact]
    public async Task StreamTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        var services = appHost.Services;
        var audioStreamer = services.GetRequiredService<AudioStreamer>();

        var streamId = (StreamId)"test-stream-id";
        var channel = Channel.CreateBounded<BlobPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

        var writeTask = ReadFromFile(channel.Writer);

        var publishTask =  audioStreamer.PublishAudioStream(streamId, channel.Reader, CancellationToken.None);
        var readTask = ReadAudioStream(streamId, audioStreamer);

        await Task.WhenAll(writeTask, readTask);
        await publishTask;

        var writtenSize = await writeTask;
        var readSize = await readTask;

        readSize.Should().Be(writtenSize);
    }

    private static async Task<int> ReadAudioStream(
        StreamId streamId,
        IAudioStreamer audioStreamer)
    {
        var audioReader = await audioStreamer.GetAudioStream(streamId, CancellationToken.None);
        return await audioReader.ReadAllAsync().SumAsync(message => message.Data.Length);
    }

    private static async Task<int> UploadRecording(Session session, string chatId, ISourceAudioRecorder sourceAudioRecorder)
    {
        var recording = new AudioRecord(
            chatId,
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
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
        var uploadTask = sourceAudioRecorder.RecordSourceAudio(session, recording, channel.Reader, CancellationToken.None);
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
