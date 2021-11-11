using System.Buffers;
using ActualChat.Blobs;
using ActualChat.Testing.Host;
using Microsoft.Extensions.DependencyInjection;
using Stl.IO;

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
        var recordSpec = new AudioRecord(
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);

        var recordTask = sourceAudioRecorder.DequeueSourceAudio(CancellationToken.None);
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordSpec, AsyncEnumerable.Empty<BlobPart>(), CancellationToken.None);

        var record = await recordTask.ConfigureAwait(false);
        record.Should().Be(recordSpec with {
            Id = record!.Id,
            AuthorId = record!.AuthorId
        });

        var audioStream = sourceAudioRecorder.GetSourceAudioBlobStream(record.Id, CancellationToken.None);
        await foreach (var _ in audioStream) {}
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
        var writtenSize = await UploadRecording(session, "1", sourceAudioRecorder);

        var record = await recordTask;
        var blobStream = sourceAudioRecorder.GetSourceAudioBlobStream(record!.Id, CancellationToken.None);
        var readSize = (long) await blobStream.SumAsync(p => p.Data.Length);

        readSize.Should().Be(writtenSize);
    }

    [Fact]
    public async Task StreamTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        var services = appHost.Services;
        var audioStreamer = services.GetRequiredService<AudioStreamer>();

        var streamId = (StreamId)"test-stream-id";
        var filePath = GetAudioFilePath();

        var blobStream = filePath.ReadBlobStream();
        var publishTask =  audioStreamer.Publish(streamId, blobStream, CancellationToken.None);
        var readTask = ReadAudioStream(streamId, audioStreamer);

        await readTask;
        await publishTask;

        var readSize = await readTask;
        readSize.Should().Be(filePath.GetFileInfo().Length);
    }

    private static async Task<long> ReadAudioStream(
        StreamId streamId,
        IAudioStreamer audioStreamer)
    {
        var blobStream = audioStreamer.GetAudioBlobStream(streamId, CancellationToken.None);
        return await blobStream.SumAsync(p => p.Data.Length);
    }

    private static async Task<long> UploadRecording(Session session, string chatId, ISourceAudioRecorder sourceAudioRecorder)
    {
        var recording = new AudioRecord(
            chatId,
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        var filePath = GetAudioFilePath();
        var blobStream = filePath.ReadBlobStream();
        await sourceAudioRecorder.RecordSourceAudio(session, recording, blobStream, CancellationToken.None);
        return filePath.GetFileInfo().Length;
    }

    private static FilePath GetAudioFilePath(FilePath? fileName = null)
        => new FilePath(Environment.CurrentDirectory) & "data" & (fileName ?? "file.webm");
}
