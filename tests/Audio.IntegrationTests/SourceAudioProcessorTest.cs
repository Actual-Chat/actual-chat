using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Host;
using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using Stl.IO;

namespace ActualChat.Audio.IntegrationTests;

public class SourceAudioProcessorTest : AppHostTestBase
{
    public SourceAudioProcessorTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task EmptyRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var sourceAudioProcessor = services.GetRequiredService<SourceAudioProcessor>();
        var sourceAudioRecorder = sourceAudioProcessor.SourceAudioRecorder;
        using var cts = new CancellationTokenSource();
        var dequeueTask = sourceAudioProcessor.SourceAudioRecorder.DequeueSourceAudio(cts.Token);

        var recordingSpec = new AudioRecord(
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordingSpec, AsyncEnumerable.Empty<RecordingPart>(), CancellationToken.None);

        var record = await dequeueTask;
        record.Should()
            .Be(recordingSpec with {
                Id = record.Id,
                AuthorId = record.AuthorId,
            });
    }

    [Fact]
    public async Task NoRecordingTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sourceAudioProcessor = services.GetRequiredService<SourceAudioProcessor>();
        var cts = new CancellationTokenSource();
        var dequeueTask = sourceAudioProcessor.SourceAudioRecorder.DequeueSourceAudio(cts.Token);
        await Task.Delay(50);
        dequeueTask.IsCompleted.Should().Be(false);
        cts.Cancel();
        await Task.Delay(50);
        dequeueTask.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task PerformRecordingAndTranscriptionTest()
    {
        using var appHost = await NewAppHost();
        var services = appHost.Services;
        var sessionFactory = services.GetRequiredService<ISessionFactory>();
        var session = sessionFactory.CreateSession();
        _ = await appHost.SignIn(session, new User("", "Bob"));
        var sourceAudioProcessor = services.GetRequiredService<SourceAudioProcessor>();
        var sourceAudioRecorder = sourceAudioProcessor.SourceAudioRecorder;
        var audioStreamer = sourceAudioProcessor.AudioSourceStreamer;
        var transcriptStreamer = sourceAudioProcessor.TranscriptStreamer;
        var chatService = services.GetRequiredService<IChats>();

        var chat = await chatService.CreateChat(new(session, "Test"), default);
        using var cts = new CancellationTokenSource();
        var sourceAudioTask = sourceAudioProcessor.SourceAudioRecorder.DequeueSourceAudio(cts.Token);
        var pushAudioTask = PushAudioData(session, chat.Id, sourceAudioRecorder);
        var audioRecord = await sourceAudioTask;

        var pipelineTask = sourceAudioProcessor.ProcessSourceAudio(audioRecord, cts.Token);
        var readTask = ReadAudioData(audioRecord.Id, audioStreamer);
        var readTranscriptTask = ReadTranscriptStream(audioRecord.Id, transcriptStreamer);
        var writtenSize = await pushAudioTask;
        var readSize = await readTask;
        var transcribed = await readTranscriptTask;
        transcribed.Should().BeGreaterThan(0);

        await pipelineTask;
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
        var sourceAudioProcessor = services.GetRequiredService<SourceAudioProcessor>();
        var sourceAudioRecorder = sourceAudioProcessor.SourceAudioRecorder;
        var audioStreamer = sourceAudioProcessor.AudioSourceStreamer;
        var chatService = services.GetRequiredService<IChats>();

        var chat = await chatService.CreateChat(new(session, "Test"), default);
        using var cts = new CancellationTokenSource();
        var recordingTask = sourceAudioProcessor.SourceAudioRecorder.DequeueSourceAudio(cts.Token);

        var pushAudioTask = PushAudioData(session, chat.Id, sourceAudioRecorder);

        var recording = await recordingTask;
        var pipelineTask = sourceAudioProcessor.ProcessSourceAudio(recording, cts.Token);
        var readTask = ReadAudioData(recording.Id, audioStreamer);
        var writtenSize = await pushAudioTask;
        var readSize = await readTask;

        await pipelineTask;

        readSize.Should().Be(writtenSize);
    }

    private static async Task<AppHost> NewAppHost()
        => await TestHostFactory.NewAppHost(
            null,
            services => {
                services.AddSingleton(new SourceAudioProcessor.Options {
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

    private static async Task<int> ReadAudioData(
        string audioRecordId,
        IAudioSourceStreamer audioStreamer)
    {
        var streamId = OpenAudioSegment.GetStreamId(audioRecordId, 0);
        var audio = await audioStreamer.GetAudio(streamId, default, CancellationToken.None);
        var header = audio.Format.Serialize();

        var sum = header.Length;
        await foreach (var audioFrame in audio.GetFrames(default))
            sum += audioFrame.Data.Length;

        return sum;
    }

    private static async Task<int> PushAudioData(
        Session session,
        string chatId,
        ISourceAudioRecorder sourceAudioRecorder)
    {
        var record = new AudioRecord(
            chatId,
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);

        var filePath = GetAudioFilePath("file.webm");
        var fileSize = (int) filePath.GetFileInfo().Length;
        var blobStream = filePath.ReadBlobStream();
        await sourceAudioRecorder.RecordSourceAudio(session, record, blobStream.ToRecordingStream(), CancellationToken.None);
        return fileSize;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
