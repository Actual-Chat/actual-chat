using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Host;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualChat.Users;
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
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;

        var audioRecord = new AudioRecord(
            session.Id, "1",
            CpuClock.Now.EpochOffset.TotalSeconds);
        await audioProcessor.ProcessAudio(audioRecord, AsyncEnumerable.Empty<AudioFrame>(), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var readSize = await ReadAudio(audioRecord.Id, audioStreamer, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(1), cts.Token);
        readSize.Should().Be(0);
        cts.Cancel();
    }

    [Fact]
    public async Task PerformRecordingAndTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;
        var transcriptStreamer = audioProcessor.TranscriptStreamer;
        var chatService = services.GetRequiredService<IChats>();
        var chatUserSettings = services.GetRequiredService<IChatUserSettings>();

        var chat = await commander.Call(new IChats.CreateChatCommand(session, "Test"));
        using var cts = new CancellationTokenSource();
        await commander.Call(new IChatUserSettings.SetCommand(session, chat.Id, new ChatUserSettings {
            Language = LanguageId.Russian,
        }), CancellationToken.None);

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, session, chat.Id);

        var readTask = ReadAudio(audioRecord.Id, audioStreamer);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);
        var readSize = await readTask;
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().BeLessThan(writtenSize);
    }

    [Fact]
    public async Task PerformRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = audioProcessor.AudioStreamer;
        var chatService = services.GetRequiredService<IChats>();

        var chat = await commander.Call(new IChats.CreateChatCommand(session, "Test"));
        using var cts = new CancellationTokenSource();

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, session, chat.Id);
        var readSize = await ReadAudio(audioRecord.Id, audioStreamer);
        readSize.Should().BeLessThan(writtenSize);
    }

    private async Task<AppHost> NewAppHost()
        => await NewAppHost(
            configureServices: services => {
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

        var sum = 0;
        await foreach (var audioFrame in audio.GetFrames(default))
            sum += audioFrame.Data.Length;

        return sum;
    }

    private async Task<(AudioRecord AudioRecord, int FileSize)> ProcessAudioFile(
        AudioProcessor audioProcessor,
        Session session,
        string chatId,
        string fileName = "file.webm",
        bool webMStream = true)
    {
        var log = audioProcessor.Commander.Services.LogFor(GetType());
        var record = new AudioRecord(
            session.Id, chatId,
            CpuClock.Now.EpochOffset.TotalSeconds);

        var filePath = GetAudioFilePath(fileName);
        var fileSize = (int)filePath.GetFileInfo().Length;
        var byteStream = filePath.ReadByteStream();
        var streamAdapter = webMStream
            ? new WebMStreamAdapter(log)
            : (IAudioStreamAdapter)new ActualOpusStreamAdapter(log);
        var audio = await streamAdapter.Read(byteStream, CancellationToken.None);
        await audioProcessor.ProcessAudio(record, audio.GetFrames(CancellationToken.None), CancellationToken.None);
        return (record, fileSize);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
