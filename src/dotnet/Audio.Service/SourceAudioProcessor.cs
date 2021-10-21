using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Transcription;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Audio;

public class SourceAudioProcessor : BackgroundService
{
    private readonly ILogger<SourceAudioProcessor> _log;

    public static bool SkipAutoStart { get; set; } = true;
    public ITranscriber Transcriber { get; }
    public AudioSegmentSaver AudioSegmentSaver { get; }
    public SourceAudioRecorder SourceAudioRecorder { get; }
    public AudioActivityExtractor AudioActivityExtractor { get; }
    public AudioSourceStreamer AudioSourceStreamer { get; }
    public TranscriptStreamer TranscriptStreamer { get; }
    public IServerSideChatService Chat { get; }
    public MomentClockSet ClockSet { get; }

    public SourceAudioProcessor(
        ITranscriber transcriber,
        AudioSegmentSaver audioSegmentSaver,
        SourceAudioRecorder sourceAudioRecorder,
        AudioActivityExtractor audioActivityExtractor,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptStreamer transcriptStreamer,
        IServerSideChatService chat,
        MomentClockSet clockSet,
        ILogger<SourceAudioProcessor> log)
    {
        _log = log;
        Transcriber = transcriber;
        AudioSegmentSaver = audioSegmentSaver;
        SourceAudioRecorder = sourceAudioRecorder;
        AudioActivityExtractor = audioActivityExtractor;
        AudioSourceStreamer = audioSourceStreamer;
        TranscriptStreamer = transcriptStreamer;
        Chat = chat;
        ClockSet = clockSet;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (SkipAutoStart)
            return;

        // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out
        while (true) {
            var record = await SourceAudioRecorder.DequeueSourceAudio(stoppingToken).ConfigureAwait(false);
            _ = ProcessSourceAudio(record, stoppingToken);
        }
    }

    internal async Task ProcessSourceAudio(AudioRecord audioRecord, CancellationToken cancellationToken)
    {
        var audioStream = SourceAudioRecorder.GetSourceAudioStream(audioRecord.Id, cancellationToken);
        var segments = AudioActivityExtractor.SplitToAudioSegments(audioRecord, audioStream, cancellationToken);
        while (await segments.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        while (segments.TryRead(out var segment)) {
            var beginsAt = ClockSet.CpuClock.UtcNow;
            var audioTask = PublishAudioStream(segment, cancellationToken);
            var textChatEntryTask = PublishTextChatEntry(segment, beginsAt, cancellationToken);
            var audioChatEntryTask = PublishAudioChatEntry(segment, beginsAt, cancellationToken);
            var transcriptTask = PublishTranscriptStream(segment, cancellationToken);
            var blobIdTask = Persist(segment, cancellationToken);
            await Task.WhenAll(audioTask, textChatEntryTask, audioChatEntryTask).ConfigureAwait(false);
            _ = UpdateTextChatEntry(textChatEntryTask, transcriptTask, cancellationToken);
            _ = UpdateAudioChatEntry(audioChatEntryTask, blobIdTask, segment, cancellationToken);
        }
    }

    private async Task<string> Persist(OpenAudioSegment openAudioSegment, CancellationToken cancellationToken)
    {
        var audioSegment = await openAudioSegment.Close().ConfigureAwait(false);
        return await AudioSegmentSaver.Save(audioSegment, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Transcript> PublishTranscriptStream(
        OpenAudioSegment segment,
        CancellationToken cancellationToken)
    {
        var transcript = Channel.CreateBounded<TranscriptUpdate>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true,
            });

        var transcribeTask = Transcribe(segment, transcript.Writer, cancellationToken);
        await TranscriptStreamer.PublishTranscriptStream(segment.StreamId, transcript.Reader, cancellationToken)
            .ConfigureAwait(false);
        return await transcribeTask.ConfigureAwait(false);
    }

    private async Task<Transcript> Transcribe(
        OpenAudioSegment segment,
        ChannelWriter<TranscriptUpdate> transcriptUpdatesWriter,
        CancellationToken cancellationToken)
    {
        var transcript = new Transcript();

        // TODO(AK): read actual config
        var request = new TranscriptionRequest(
            segment.StreamId,
            new AudioFormat {
                CodecKind = AudioCodecKind.Opus,
                ChannelCount = 1,
                SampleRate = 48_000,
            },
            new TranscriptionOptions {
                Language = "ru-RU",
                IsDiarizationEnabled = false,
                IsPunctuationEnabled = true,
                MaxSpeakerCount = 1,
            });
        var audioSource = segment.Source;
        var updates = await Transcriber.Transcribe(request, audioSource, cancellationToken).ConfigureAwait(false);

        Exception? error = null;
        try {
            while (await updates.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (updates.TryRead(out var update)) {
                transcript = transcript.WithUpdate(update);
                await transcriptUpdatesWriter.WriteAsync(update, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException) { }
        catch (Exception e) {
            error = e;
        }
        finally {
            transcriptUpdatesWriter.TryComplete(error);
        }
        return transcript;
    }

    private async Task<ChatEntry> PublishTextChatEntry(
        OpenAudioSegment openAudioSegment,
        Moment beginsAt,
        CancellationToken cancellationToken)
    {
        var chatEntry = new ChatEntry(openAudioSegment.AudioRecord.ChatId, 0) {
            AuthorId = openAudioSegment.AudioRecord.UserId,
            Content = "...",
            ContentType = ChatContentType.Text,
            StreamId = openAudioSegment.StreamId,
            BeginsAt = beginsAt,
        };
        return await Chat.CreateEntry(new ChatCommands.CreateEntry(chatEntry).MarkServerSide(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChatEntry> PublishAudioChatEntry(
        OpenAudioSegment openAudioSegment,
        Moment beginsAt,
        CancellationToken cancellationToken)
    {
        var chatEntry = new ChatEntry(openAudioSegment.AudioRecord.ChatId, 0) {
            AuthorId = openAudioSegment.AudioRecord.UserId,
            Content = "",
            ContentType = ChatContentType.Audio,
            StreamId = openAudioSegment.StreamId,
            BeginsAt = beginsAt,
        };
        return await Chat.CreateEntry(new ChatCommands.CreateEntry(chatEntry).MarkServerSide(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateTextChatEntry(
        Task<ChatEntry> chatEntryTask,
        Task<Transcript> transcriptTask,
        CancellationToken cancellationToken)
    {
        var chatEntry = await chatEntryTask.ConfigureAwait(false);
        var transcript = await transcriptTask.ConfigureAwait(false);

        var updated = chatEntry with {
            Content = transcript.Text,
            StreamId = StreamId.None,
            EndsAt = chatEntry.BeginsAt.ToDateTime().AddSeconds(transcript.Duration),
            TextToTimeMap = transcript.TextToTimeMap,
        };
        await Chat.UpdateEntry(new ChatCommands.UpdateEntry(updated).MarkServerSide(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateAudioChatEntry(
        Task<ChatEntry> chatEntryTask,
        Task<string> blobIdTask,
        OpenAudioSegment segment,
        CancellationToken cancellationToken)
    {
        var chatEntry = await chatEntryTask.ConfigureAwait(false);
        var blobId = await blobIdTask.ConfigureAwait(false);

        var duration = await segment.DurationTask;
        var updated = chatEntry with {
            Content = blobId,
            StreamId = StreamId.None,
            EndsAt = chatEntry.BeginsAt.ToDateTime().Add(duration),
        };
        await Chat.UpdateEntry(new ChatCommands.UpdateEntry(updated).MarkServerSide(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PublishAudioStream(OpenAudioSegment segment, CancellationToken cancellationToken)
        => await AudioSourceStreamer.PublishAudioSource(segment.StreamId, segment.Source, cancellationToken)
            .ConfigureAwait(false);
}
