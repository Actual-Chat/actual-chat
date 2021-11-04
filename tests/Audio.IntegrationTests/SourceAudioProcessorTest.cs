using System.Buffers;
using ActualChat.Blobs;
using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using Microsoft.Extensions.DependencyInjection;

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

        var channel = Channel.CreateBounded<BlobPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });
        var recordingSpec = new AudioRecord(
            "1",
            new AudioFormat { CodecKind = AudioCodecKind.Opus, ChannelCount = 1, SampleRate = 48_000 },
            "RU-ru",
            CpuClock.Now.EpochOffset.TotalSeconds);
        _ = sourceAudioRecorder.RecordSourceAudio(session, recordingSpec, channel.Reader, CancellationToken.None);
        channel.Writer.Complete();

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
        var chatService = services.GetRequiredService<IChatServiceFrontend>();

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
        var chatService = services.GetRequiredService<IChatServiceFrontend>();

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
        var updates = await transcriptStreamer.GetTranscriptStream(streamId, CancellationToken.None);
        await foreach (var update in updates.ReadAllAsync()) {
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
        var audioSource = await audioStreamer.GetAudioSource(streamId, default, CancellationToken.None);
        var header = Convert.FromBase64String(audioSource.Format.CodecSettings);

        var sum = header.Length;
        await foreach (var audioFrame in audioSource)
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
        var channel = Channel.CreateBounded<BlobPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

        _ = sourceAudioRecorder.RecordSourceAudio(session, record, channel.Reader, CancellationToken.None);

        var size = 0;
        await using var inputStream = new FileStream(Path.Combine(Environment.CurrentDirectory, "data", "file.webm"),
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
            await channel.Writer.WriteAsync(command, CancellationToken.None);

            // await Task.Delay(300); //emulate real-time speech delay
            bytesRead = await inputStream.ReadAsync(readBuffer);
            size += bytesRead;
        }

        channel.Writer.Complete();
        return size;
    }
}
