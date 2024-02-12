using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.App.Server;
using ActualChat.IO;
using ActualChat.Kvas;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.IO;
using ActualLab.Mathematics;

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
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var accountSettings = services.AccountSettings(session);
        if (mustSetUserLanguageSettings)
            await accountSettings.SetUserLanguageSettings(new () { Primary = Languages.Main, }, CancellationToken.None);

        var audioRecord = AudioRecord.New(session, Constants.Chat.DefaultChatId,
            CpuClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);
        await audioProcessor.ProcessAudio(audioRecord, 333, AsyncEnumerable.Empty<AudioFrame>(), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var readSize = await ReadAudio(audioRecord.Id, audioStreamer, default, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(1), cts.Token);
        readSize.Should().Be(0);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task PerformRecordingAndTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var transcriptStreamer = services.GetRequiredService<ITranscriptStreamer>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var accountSettings = services.AccountSettings(session);
        await accountSettings.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var userChatSettings = new UserChatSettings { Language = Languages.Russian };
        await accountSettings.SetUserChatSettings(chat.Id, userChatSettings, CancellationToken.None);
        var audioRecord = AudioRecord.New(session, chat.Id, SystemClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);
        var ctsToken = cts.Token;
        var readTask = BackgroundTask.Run(
            () => ReadAudio(audioRecord.Id, audioStreamer, default, ctsToken),
            ctsToken);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);

        var writtenSize = await ProcessAudioFile(audioRecord, audioProcessor, log);

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
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var transcriptStreamer = services.GetRequiredService<ITranscriptStreamer>();
        var chats = services.GetRequiredService<IChatsBackend>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var accountSettings = services.AccountSettings(session);
        await accountSettings.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var userChatSettings = new UserChatSettings { Language = Languages.Russian };
        await accountSettings.SetUserChatSettings(chat.Id, userChatSettings, CancellationToken.None);
        var audioRecord = AudioRecord.New(session, chat.Id, SystemClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);

        var ctsToken = cts.Token;
        var readTask = BackgroundTask.Run(
            () => ReadAudio(audioRecord.Id, audioStreamer, default, ctsToken),
            ctsToken);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);

        var writtenSize = await ProcessAudioFile(
            audioRecord,
            audioProcessor,
            log,
            "0000.opuss",
            false);

        var readSize = await readTask;
        readSize.Should().BeGreaterThan(100);
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().BeLessOrEqualTo(writtenSize);

        var idRange = await chats.GetIdRange(chat.Id, ChatEntryKind.Text, true, CancellationToken.None);
        var lastIdTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var lastTile = await chats.GetTile(
            chat.Id,
            ChatEntryKind.Text,
            lastIdTile.Range,
            true,
            CancellationToken.None);
        var lastEntry = lastTile.Entries[^1];
        lastEntry.IsRemoved.Should().BeFalse();
        lastEntry.Content.Should().Be("И");
    }

    [Fact(Skip = "For manual runs only")]
    public async Task LongTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer = services.GetRequiredService<IAudioStreamer>();
        var transcriptStreamer = services.GetRequiredService<ITranscriptStreamer>();
        var chats = services.GetRequiredService<IChatsBackend>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var accountSettings = services.AccountSettings(session);
        await accountSettings.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var userChatSettings = new UserChatSettings { Language = Languages.Russian };
        await accountSettings.SetUserChatSettings(chat.Id, userChatSettings, CancellationToken.None);
        var audioRecord = AudioRecord.New(session, chat.Id, SystemClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);

        var ctsToken = cts.Token;
        var readTask = BackgroundTask.Run(
            () => ReadAudio(audioRecord.Id, audioStreamer, default, ctsToken),
            ctsToken);
        var readTranscriptTask = BackgroundTask.Run(
            () => ReadTranscriptStream(audioRecord.Id, transcriptStreamer));

        var writtenSize = await ProcessAudioFile(
            audioRecord,
            audioProcessor,
            log,
            "large-file-0.opuss",
            true);

        var readSize = await readTask;
        readSize.Should().BeGreaterThan(100);
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);
        readSize.Should().BeLessOrEqualTo(writtenSize);

        var idRange = await chats.GetIdRange(chat.Id, ChatEntryKind.Text, true, CancellationToken.None);
        var lastIdTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(idRange.End - 1);
        var lastTile = await chats.GetTile(
            chat.Id,
            ChatEntryKind.Text,
            lastIdTile.Range,
            true,
            CancellationToken.None);
        var lastEntry = lastTile.Entries[^1];
        lastEntry.IsRemoved.Should().BeFalse();
        lastEntry.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PerformRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer =services.GetRequiredService<IAudioStreamer>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var accountSettings = services.AccountSettings(session);
        await accountSettings.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var audioRecord = AudioRecord.New(session, chat.Id, SystemClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);
        var ctsToken = cts.Token;
        var readSizeTask = BackgroundTask.Run(
            () => ReadAudio(audioRecord.Id, audioStreamer, default, ctsToken),
            ctsToken);

        var writtenSize = await ProcessAudioFile(audioRecord, audioProcessor, log);

        var readSize = await readSizeTask;
        readSize.Should().BeLessThan(writtenSize);
    }

    [Fact]
    public async Task RealtimeAudioStreamerSupportsSkip()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new User("Bob"));
        var audioProcessor = services.GetRequiredService<AudioProcessor>();
        var audioStreamer =services.GetRequiredService<IAudioStreamer>();
        var log = services.GetRequiredService<ILogger<AudioProcessorTest>>();
        var accountSettings = services.AccountSettings(session);
        await accountSettings.Set(UserLanguageSettings.KvasKey,
            new UserLanguageSettings {
                Primary = Languages.Russian,
            });

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "Test",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource();

        var audioRecord = AudioRecord.New(session, chat.Id, SystemClock.Now.EpochOffset.TotalSeconds, ChatEntryId.None);
        var ctsToken = cts.Token;
        var readSizeTask = BackgroundTask.Run(
            () => ReadAudio(audioRecord.Id, audioStreamer, TimeSpan.FromSeconds(1), ctsToken),
            ctsToken);

        var writtenSize = await ProcessAudioFile(audioRecord, audioProcessor, log);

        var readSize = await readSizeTask;
        readSize.Should().BeLessThan(writtenSize);
    }

    private async Task<AppHost> NewAppHost()
        => await NewAppHost(TestAppHostOptions.Default with {
            AppServicesExtender = (_, services) => {
                services.AddSingleton(new AudioProcessor.Options {
                    IsEnabled = false,
                });
            },
        });

    private async Task<int> ReadTranscriptStream(
        string audioRecordId,
        ITranscriptStreamer transcriptStreamer)
    {
        var audioStreamId = OpenAudioSegment.GetStreamId(audioRecordId, 0);
        var transcriptStreamId = audioStreamId;
        var diffs = transcriptStreamer.GetTranscriptDiffStream(transcriptStreamId, CancellationToken.None);
        var transcript = Transcript.Empty;
        var length = 0;
        await foreach (var diff in diffs) {
            transcript += diff;
            Out.WriteLine($"TextDiff: {diff.TextDiff}");
            Out.WriteLine($"Transcript: {transcript}");
            length = transcript.Length;
        }
        return length;
    }

    private static async Task<int> ReadAudio(
        string audioRecordId,
        IAudioStreamer audioStreamer,
        TimeSpan skip = default,
        CancellationToken cancellationToken = default)
    {
        var streamId = OpenAudioSegment.GetStreamId(audioRecordId, 0);
        var audio = await audioStreamer.GetAudio(streamId, skip, cancellationToken);

        var sum = 0;
        await foreach (var audioFrame in audio.GetFrames(default))
            sum += audioFrame.Data.Length;

        return sum;
    }

    private async Task<int> ProcessAudioFile(
        AudioRecord audioRecord,
        AudioProcessor audioProcessor,
        ILogger log,
        string fileName = "file.webm",
        bool withDelay = false)
    {
        var audio = await GetAudio(fileName, log, withDelay);
        var filePath = GetAudioFilePath(fileName);
        var fileSize = (int)filePath.GetFileInfo().Length;
        await audioProcessor.ProcessAudio(audioRecord, 222, audio.GetFrames(CancellationToken.None), CancellationToken.None);
        return fileSize;
    }

    private async Task<AudioSource> GetAudio(
        FilePath fileName,
        ILogger log,
        bool withDelay = false)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = fileName.Extension == ".webm";
        var converter = isWebMStream
            ? (IAudioStreamConverter)new WebMStreamConverter(MomentClockSet.Default, log)
            : new ActualOpusStreamConverter(MomentClockSet.Default, log);
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        if (!withDelay)
            return audio;

        var delayedFrames = audio.GetFrames(CancellationToken.None)
            .SelectAwait(async f => {
                await Task.Delay(20);
                return f;
            });
        var delayedAudio = new AudioSource(
            MomentClockSet.Default.SystemClock.Now,
            audio.Format,
            delayedFrames,
            TimeSpan.Zero,
            log,
            CancellationToken.None);

        return delayedAudio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
