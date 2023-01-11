using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.App.Server;
using ActualChat.Kvas;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualChat.Users;
using Stl.IO;
using Stl.Mathematics;

namespace ActualChat.Audio.IntegrationTests;

public class AudioProcessorTest : AppHostTestBase
{
    public AudioProcessorTest(ITestOutputHelper @out) : base(@out) { }

    [Theory(Skip = "Flaky")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyRecordingTest(bool mustSetUserLanguageSettings)
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.SessionFactory();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var kvas = new ServerKvasClient(services.GetRequiredService<IServerKvas>(), session);
        if (mustSetUserLanguageSettings)
            await kvas.SetUserLanguageSettings(new () { Primary = Languages.Main, }, CancellationToken.None);

        var audioRecord = new AudioRecord(
            session, Constants.Chat.DefaultChatId,
            CpuClock.Now.EpochOffset.TotalSeconds);
        await audioProcessor.ProcessAudio(audioRecord, 333, AsyncEnumerable.Empty<AudioFrame>(), CancellationToken.None);

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
        var sessionFactory = services.SessionFactory();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var transcriptStreamer = services.GetRequiredService<ITranscriptStreamer>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var kvas = new ServerKvasClient(services.GetRequiredService<IServerKvas>(), session);
        await kvas.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var userChatSettings = new UserChatSettings { Language = Languages.Russian };
        await kvas.SetUserChatSettings(chat.Id, userChatSettings, CancellationToken.None);

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, log, session, chat.Id);

        var readTask = ReadAudio(audioRecord.Id, audioStreamer, cts.Token);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);
        var readSize = await readTask;
        readSize.Should().BeGreaterThan(100);
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().BeLessOrEqualTo(writtenSize);
    }

    [Fact]
    public async Task ShortTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var sessionFactory = services.SessionFactory();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var transcriptStreamer = services.GetRequiredService<ITranscriptStreamer>();
        var chats = services.GetRequiredService<IChatsBackend>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var kvas = new ServerKvasClient(services.GetRequiredService<IServerKvas>(), session);
        await kvas.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var userChatSettings = new UserChatSettings { Language = Languages.Russian };
        await kvas.SetUserChatSettings(chat.Id, userChatSettings, CancellationToken.None);

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor,
            log,
            session,
            chat.Id,
            "0000.opuss",
            false);

        var readTask = ReadAudio(audioRecord.Id, audioStreamer, cts.Token);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);
        var readSize = await readTask;
        readSize.Should().BeGreaterThan(100);
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().BeLessOrEqualTo(writtenSize);

        var idRange = await chats.GetIdRange(chat.Id, ChatEntryKind.Text, true, CancellationToken.None);
        var lastIdTile = Constants.Chat.IdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var lastTile = await chats.GetTile(
            chat.Id,
            ChatEntryKind.Text,
            lastIdTile.Range,
            true,
            CancellationToken.None);
        var lastEntry = lastTile.Entries.Last();
        lastEntry.IsRemoved.Should().BeFalse();
        lastEntry.Content.Should().Be("И раз");
    }

    [Fact]
    public async Task PerformRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var sessionFactory = services.SessionFactory();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer =services.GetRequiredService<IAudioStreamer>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var kvas = new ServerKvasClient(services.GetRequiredService<IServerKvas>(), session);
        await kvas.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new IChats.ChangeCommand(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var (audioRecord, writtenSize) = await ProcessAudioFile(audioProcessor, log, session, chat.Id);
        var readSize = await ReadAudio(audioRecord.Id, audioStreamer, cts.Token);
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
        ILogger log,
        Session session,
        ChatId chatId,
        string fileName = "file.webm",
        bool webMStream = true)
    {
        var record = new AudioRecord(session, chatId, SystemClock.Now.EpochOffset.TotalSeconds);

        var filePath = GetAudioFilePath(fileName);
        var fileSize = (int)filePath.GetFileInfo().Length;
        var byteStream = filePath.ReadByteStream();
        var streamAdapter = webMStream
            ? new WebMStreamAdapter(log)
            : (IAudioStreamAdapter)new ActualOpusStreamAdapter(log);
        var audio = await streamAdapter.Read(byteStream, CancellationToken.None);
        await audioProcessor.ProcessAudio(record, 222, audio.GetFrames(CancellationToken.None), CancellationToken.None);
        return (record, fileSize);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
