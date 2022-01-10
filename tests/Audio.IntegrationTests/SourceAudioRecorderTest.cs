using ActualChat.Host;
using ActualChat.Media;
using ActualChat.Testing.Host;
using Stl.IO;

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
            session.Id, "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);

        var recordTask = sourceAudioRecorder.DequeueSourceAudio(CancellationToken.None);
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordSpec, AsyncEnumerable.Empty< RecordingPart>(), CancellationToken.None);

        var record = await recordTask.ConfigureAwait(false);
        record.Should().Be(recordSpec with {
            Id = record.Id,
            SessionId = record.SessionId,
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
        var byteStream = sourceAudioRecorder.GetSourceAudioRecordingStream(record.Id, CancellationToken.None);
        var readSize = (long) await byteStream.SumAsync(p => p.Data?.Length ?? 0);

        readSize.Should().Be(writtenSize);
    }


    private static async Task<long> UploadRecording(Session session, string chatId, ISourceAudioRecorder sourceAudioRecorder)
    {
        var recording = new AudioRecord(
            session.Id, chatId,
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            CpuClock.Now.EpochOffset.TotalSeconds);
        var filePath = GetAudioFilePath();
        var byteStream = filePath.ReadByteStream();
        await sourceAudioRecorder.RecordSourceAudio(session, recording, byteStream.ToRecordingStream(), CancellationToken.None);
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
