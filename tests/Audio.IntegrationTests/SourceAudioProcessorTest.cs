using ActualChat.Blobs;
using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Stl.IO;

namespace ActualChat.Audio.IntegrationTests;

public class SourceAudioProcessorTest : AppHostTestBase
{
    public SourceAudioProcessorTest(ITestOutputHelper @out) : base(@out)
        => SourceAudioProcessor.SkipAutoStart = true;

    [Fact]
    public async Task EmptyRecordingTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
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
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordingSpec, AsyncEnumerable.Empty<BlobPart>(), CancellationToken.None);

        var record = await dequeueTask;
        record.Should()
            .Be(recordingSpec with {
                Id = record!.Id,
                AuthorId = record!.AuthorId,
            });
    }

    [Fact]
    public async Task NoRecordingTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
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
        using var appHost = await TestHostFactory.NewAppHost();
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
        var recordingTask = sourceAudioProcessor.SourceAudioRecorder.DequeueSourceAudio(cts.Token);

        var pushAudioTask = PushAudioData(session, chat.Id, sourceAudioRecorder);

        var recording = await recordingTask;
        var pipelineTask = sourceAudioProcessor.ProcessSourceAudio(recording!, cts.Token);

        var readTask = ReadAudioData(recording!.Id, audioStreamer);
        var readTranscriptTask = ReadTranscriptStream(recording!.Id, transcriptStreamer);
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
        using var appHost = await TestHostFactory.NewAppHost();
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
        var pipelineTask = sourceAudioProcessor.ProcessSourceAudio(recording!, cts.Token);

        var readTask = ReadAudioData(recording!.Id, audioStreamer);
        var writtenSize = await pushAudioTask;
        var readSize = await readTask;

        await pipelineTask;

        readSize.Should().Be(writtenSize);
    }

    private async Task<int> ReadTranscriptStream(
        AudioRecordId audioRecordId,
        ITranscriptStreamer transcriptStreamer)
    {
        var size = 0;
        // TODO(AK): we need to figure out how to notify consumers about new streamID - with new ChatEntry?
        var streamId = new StreamId(audioRecordId, 0);
        var transcriptStream = transcriptStreamer.GetTranscriptStream(streamId, CancellationToken.None);
        await foreach (var update in transcriptStream) {
            if (update.UpdatedPart == null)
                continue;

            Out.WriteLine(update.UpdatedPart.Text);
            size = (int)update.UpdatedPart.TextToTimeMap.SourceRange.Max;
        }
        return size;
    }

    private static async Task<int> ReadAudioData(
        AudioRecordId audioRecordId,
        IAudioSourceStreamer audioStreamer)
    {
        var streamId = new StreamId(audioRecordId, 0);
        var audio = await audioStreamer.GetAudio(streamId, default, CancellationToken.None);
        var header = audio.Format.ToBlobPart().Data;

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
        await sourceAudioRecorder.RecordSourceAudio(session, record, blobStream, CancellationToken.None);
        return fileSize;
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName).ReadBlobStream(cancellationToken);
        var audio = new AudioSource(blobStream, default, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
