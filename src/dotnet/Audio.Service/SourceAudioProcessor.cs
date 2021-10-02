using System.Text;
using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Transcription;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Audio;

public class SourceAudioProcessor : BackgroundService
{
    public static bool SkipAutoStart { get; set; } = true;

    private readonly ILogger<SourceAudioProcessor> _log;
    public ITranscriber Transcriber { get; }
    public AudioSaver AudioSaver { get; }
    public SourceAudioRecorder SourceAudioRecorder { get; }
    public AudioActivityExtractor AudioActivityExtractor { get; }
    public AudioStreamer AudioStreamer { get; }
    public TranscriptStreamer TranscriptStreamer { get; }
    public IServerSideChatService Chat { get; }

    public SourceAudioProcessor(
        ITranscriber transcriber,
        AudioSaver audioSaver,
        SourceAudioRecorder sourceAudioRecorder,
        AudioActivityExtractor audioActivityExtractor,
        AudioStreamer audioStreamer,
        TranscriptStreamer transcriptStreamer,
        IServerSideChatService chat,
        ILogger<SourceAudioProcessor> log)
    {
        _log = log;
        Transcriber = transcriber;
        AudioSaver = audioSaver;
        SourceAudioRecorder = sourceAudioRecorder;
        AudioActivityExtractor = audioActivityExtractor;
        AudioStreamer = audioStreamer;
        TranscriptStreamer = transcriptStreamer;
        Chat = chat;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (SkipAutoStart)
            return;

        // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out
        while (true) {
            var record = await SourceAudioRecorder.DequeueSourceAudio(stoppingToken);
            _ = ProcessSourceAudio(record!, stoppingToken);
        }
    }

    internal async Task ProcessSourceAudio(AudioRecord audioRecord, CancellationToken cancellationToken)
    {
        var audioStream = SourceAudioRecorder.GetSourceAudioStream(audioRecord.Id, cancellationToken);
        var segments = AudioActivityExtractor.GetSegmentsWithAudioActivity(audioRecord, audioStream, cancellationToken);
        while (await segments.WaitToReadAsync(cancellationToken))
        while (segments.TryRead(out var segment)) {
            var audioTask = PublishAudioStream(segment, cancellationToken);
            var chatEntryTask = PublishChatEntry(segment, cancellationToken);
            var transcriptTask = PublishTranscriptStream(segment, cancellationToken);
            _ = Persist(segment, cancellationToken);
            await Task.WhenAll(audioTask, chatEntryTask).ConfigureAwait(false);
            _ = UpdateChatEntry(chatEntryTask, transcriptTask, cancellationToken);
        }
    }

    private async Task Persist(AudioRecordSegment segment, CancellationToken cancellationToken)
    {
        var audioStreamPart = await segment.GetAudioStreamPart();
        await AudioSaver.Save(audioStreamPart, cancellationToken);
    }

    private async Task<string> PublishTranscriptStream(AudioRecordSegment segment, CancellationToken cancellationToken)
    {
        var transcript = Channel.CreateBounded<TranscriptPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

        var transcribeTask = Transcribe(segment, transcript.Writer, cancellationToken);
        await TranscriptStreamer.PublishTranscriptStream(segment.StreamId, transcript.Reader, cancellationToken).ConfigureAwait(false);
        return await transcribeTask.ConfigureAwait(false);
    }

    private async Task<string> Transcribe(
        AudioRecordSegment segment,
        ChannelWriter<TranscriptPart> transcriptWriter,
        CancellationToken cancellationToken)
    {
        var transcriptBuilder = new StringBuilder();

        // TODO(AK): read actual config
        var request = new TranscriptionRequest(
            segment.StreamId,
            new AudioFormat {
                Codec = AudioCodec.Opus,
                ChannelCount = 1,
                SampleRate = 48_000,
            },
            new TranscriptionOptions {
                Language = "ru-RU",
                IsDiarizationEnabled = false,
                IsPunctuationEnabled = true,
                MaxSpeakerCount = 1,
            });
        var audioStream = await segment.GetAudioStream().ConfigureAwait(false);
        var transcriptResult = await Transcriber.Transcribe(request, audioStream, cancellationToken).ConfigureAwait(false);

        Exception? error = null;
        try {
            while (await transcriptResult.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            while (transcriptResult.TryRead(out var speechFragment)) {
                if (speechFragment.IsFinal) {
                    transcriptBuilder.Append(speechFragment.Text);
                    transcriptBuilder.Append(' ');
                }
                var transcriptPart = new TranscriptPart(
                    speechFragment.Text,
                    speechFragment.TextIndex,
                    speechFragment.StartOffset,
                    speechFragment.Duration);
                await transcriptWriter.WriteAsync(transcriptPart, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException) { }
        catch (Exception e) {
            error = e;
        }
        finally {
            transcriptWriter.TryComplete(error);
        }

        return transcriptBuilder.ToString();
    }

    private async Task<ChatEntry> PublishChatEntry(
        AudioRecordSegment audioRecordSegment,
        CancellationToken cancellationToken)
    {
        var e = audioRecordSegment;
        var chatEntry = new ChatEntry(e.AudioRecord.ChatId, 0) {
            AuthorId = e.AudioRecord.UserId,
            Content = "...",
            ContentType = ChatContentType.Text,
            StreamId = e.StreamId,
        };
        return await Chat.CreateEntry( new ChatCommands.CreateEntry(chatEntry).MarkServerSide(), cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateChatEntry(
        Task<ChatEntry> chatEntryTask,
        Task<string> transcriptTask,
        CancellationToken cancellationToken)
    {
        var chatEntry = await chatEntryTask.ConfigureAwait(false);
        var transcript = await transcriptTask.ConfigureAwait(false);

        var updated = chatEntry with { Content = transcript, StreamId = StreamId.None };
        await Chat.UpdateEntry(new ChatCommands.UpdateEntry(updated).MarkServerSide(), cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishAudioStream(AudioRecordSegment segment, CancellationToken cancellationToken)
        => await AudioStreamer.PublishAudioStream(segment.StreamId, await segment.GetAudioStream(), cancellationToken).ConfigureAwait(false);
}
