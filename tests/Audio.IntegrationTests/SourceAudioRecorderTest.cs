using ActualChat.Audio.Db;
using ActualChat.Host;
using ActualChat.Media;
using ActualChat.Testing.Host;
using Stl.IO;
using Stl.Redis;

namespace ActualChat.Audio.IntegrationTests;

public class SourceAudioRecorderTest : AppHostTestBase
{
    public SourceAudioRecorderTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async void EmptyRecordingTest()
    {
        using var appHost = await NewAppHost();
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
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordSpec, AsyncEnumerable.Empty< RecordingPart>(), CancellationToken.None);

        var record = await recordTask.ConfigureAwait(false);
        record.Should().Be(recordSpec with {
            Id = record.Id,
            AuthorId = record.AuthorId
        });

        var audioStream = sourceAudioRecorder.GetSourceAudioRecordingStream(record.Id, CancellationToken.None);
        await foreach (var _ in audioStream) {}
    }

    [Fact]
    public async Task StreamRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var sourceAudioRecorder = services.GetRequiredService<SourceAudioRecorder>();

        var recordTask = sourceAudioRecorder.DequeueSourceAudio(CancellationToken.None);
        var writtenSize = await UploadRecording(session, "1", sourceAudioRecorder);

        var record = await recordTask;
        var blobStream = sourceAudioRecorder.GetSourceAudioRecordingStream(record.Id, CancellationToken.None);
        var readSize = (long) await blobStream.SumAsync(p => p.Data.Length);

        readSize.Should().Be(writtenSize);
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
        await sourceAudioRecorder.RecordSourceAudio(session, recording, blobStream.ToRecordingStream(), CancellationToken.None);
        return filePath.GetFileInfo().Length;
    }

    private static FilePath GetAudioFilePath(FilePath? fileName = null)
        => new FilePath(Environment.CurrentDirectory) & "data" & (fileName ?? "file.webm");

    private static async Task<AppHost> NewAppHost()
        => await TestHostFactory.NewAppHost(
            null,
            services => {
                services.AddSingleton(new SourceAudioProcessor.Options {
                    IsEnabled = false,
                });
            });
}
