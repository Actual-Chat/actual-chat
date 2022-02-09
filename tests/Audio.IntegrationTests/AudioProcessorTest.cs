using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Host;
using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using Stl.IO;

namespace ActualChat.Audio.IntegrationTests;

public class AudioProcessorTest : AppHostTestBase
{
    public AudioProcessorTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task EmptyRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;

        var audioRecord = new AudioRecord(
            session.Id, "1",
            CpuClock.Now.EpochOffset.TotalSeconds);
        await audioProcessor.ProcessAudio(audioRecord, AsyncEnumerable.Empty<RecordingPart>(), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var readSizeOpt = await ReadAudio(audioRecord.Id, audioStreamer, cts.Token)
            .WithTimeout(TimeSpan.FromSeconds(1), CancellationToken.None);

        readSizeOpt.HasValue.Should().BeFalse();
        cts.Cancel();
    }

    [Fact]
    public async Task PerformRecordingAndTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;
        var transcriptStreamer = audioProcessor.TranscriptStreamer;
        var chatService = services.GetRequiredService<IChats>();

        var chat = await chatService.CreateChat(new(session, "Test"), default);
        using var cts = new CancellationTokenSource();
        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, session, chat.Id);

        var readTask = ReadAudio(audioRecord.Id, audioStreamer);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);
        var readSize = await readTask;
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().Be(writtenSize);
    }

    [Fact]
    public async Task PerformRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;
        var chatService = services.GetRequiredService<IChats>();

        var chat = await chatService.CreateChat(new(session, "Test"), default);
        using var cts = new CancellationTokenSource();

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, session, chat.Id);
        var readSize = await ReadAudio(audioRecord.Id, audioStreamer);
        readSize.Should().Be(writtenSize);
    }

    private static async Task<AppHost> NewAppHost()
        => await TestHostFactory.NewAppHost(
            null,
            services => {
                services.AddSingleton(new AudioProcessor.Options {
                    IsEnabled = false,
                });
            });

    private async Task<int> ReadTranscriptStream(
        string audioRecordId,
        ITranscriptStreamer transcriptStreamer)
    {
        var totalLength = 0;
        // TODO(AK): we need to figure out how to notify consumers about new streamId - with new ChatEntry?
        var audioStreamId = OpenAudioSegment.GetStreamId(audioRecordId, 0);
        var transcriptStreamId = TranscriptSegment.GetStreamId(audioStreamId, 0);
        var diffs = transcriptStreamer.GetTranscriptDiffStream(transcriptStreamId, CancellationToken.None);
        await foreach (var diff in diffs) {
            Out.WriteLine(diff.Text);
            totalLength += diff.Length;
        }
        return totalLength;
    }

    private static async Task<int> ReadAudio(
        string audioRecordId,
        IAudioStreamer audioStreamer,
        CancellationToken cancellationToken = default)
    {
        var streamId = OpenAudioSegment.GetStreamId(audioRecordId, 0);
        var audio = await audioStreamer.GetAudio(streamId, default, cancellationToken);
        var header = audio.Format.Serialize();

        var sum = header.Length;
        await foreach (var audioFrame in audio.GetFrames(default))
            sum += audioFrame.Data.Length;

        return sum;
    }

    private static async Task<(AudioRecord AudioRecord, int FileSize)> ProcessAudioFile(
        AudioProcessor audioProcessor,
        Session session,
        string chatId,
        string fileName = "file.webm")
    {
        var record = new AudioRecord(
            session.Id, chatId,
            CpuClock.Now.EpochOffset.TotalSeconds);

        var filePath = GetAudioFilePath(fileName);
        var fileSize = (int) filePath.GetFileInfo().Length;
        var byteStream = filePath.ReadByteStream();
        await audioProcessor.ProcessAudio(record, byteStream.ToRecordingStream(), CancellationToken.None);
        return (record, fileSize);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
