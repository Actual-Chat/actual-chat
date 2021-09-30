using ActualChat.Audio.Processing;
using ActualChat.Blobs;
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
            var segmentAudioStreamTask = PublishAudioStream(segment, cancellationToken);
            var chatEntryTask = PublishChatEntry(segment, cancellationToken);
            _ = PublishTranscriptStream(segment, cancellationToken);
            _ = Persist(segment, cancellationToken);
            await Task.WhenAll(segmentAudioStreamTask, chatEntryTask);
        }
    }

    private async Task Persist(AudioRecordSegment segment, CancellationToken cancellationToken)
    {
        var audioStreamPart = await segment.GetAudioStreamPart();
        await AudioSaver.Save(audioStreamPart, cancellationToken);
    }

    private async Task PublishTranscriptStream(AudioRecordSegment segment, CancellationToken cancellationToken)
    {
        var transcript = Channel.CreateBounded<TranscriptPart>(
            new BoundedChannelOptions(100) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

        _ = TranscribeSegment(segment, transcript.Writer, cancellationToken);
        await TranscriptStreamer.PublishTranscriptStream(segment.StreamId, transcript.Reader, cancellationToken);
    }

    private async Task TranscribeSegment(
        AudioRecordSegment segment,
        ChannelWriter<TranscriptPart> target,
        CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            var audioRecord = segment.AudioRecord;
            // TODO(AK): read actual config
            var command = new BeginTranscriptionCommand {
                RecordId = (string) audioRecord.Id,
                AudioFormat = new AudioFormat {
                    Codec = AudioCodec.Opus,
                    ChannelCount = 1,
                    SampleRate = 48_000
                },
                Options = new TranscriptionOptions {
                    Language = "ru-RU",
                    IsDiarizationEnabled = false,
                    IsPunctuationEnabled = true,
                    MaxSpeakerCount = 1
                }
            };
            var transcriptId = await Transcriber.BeginTranscription(command, cancellationToken);

            var audioStream = await segment.GetAudioStream();
            _ = Transcribe(transcriptId, audioStream, cancellationToken);

            var index = 0;
            var result = await Transcriber.PollTranscription(new PollTranscriptionCommand(transcriptId, index), cancellationToken);
            while (result.ContinuePolling && !cancellationToken.IsCancellationRequested) {
                foreach (var fragmentVariant in result.Fragments) {
                    if (fragmentVariant.Speech is { } speechFragment) {
                        var message = new TranscriptPart(
                            speechFragment.Text,
                            speechFragment.TextIndex,
                            speechFragment.StartOffset,
                            speechFragment.Duration);
                        await target.WriteAsync(message, cancellationToken);
                    }
                    else if (fragmentVariant.Error != null) {
                        // TODO(AK) - think about additional scenarios of transcription error handling
                        _log.LogError("Transcription error: {TranscriptError}", fragmentVariant.Error.Message);
                    }
                    index = fragmentVariant.Value!.Index + 1;
                }

                result = await Transcriber.PollTranscription(
                    new PollTranscriptionCommand(transcriptId, index),
                    cancellationToken);
            }

            await Transcriber.AckTranscription(new AckTranscriptionCommand(transcriptId, index), cancellationToken);

        }
        catch (Exception e) {
            error = e;
        }
        finally {
            target.Complete(error);
        }

        async Task Transcribe(Symbol transcriptId1, ChannelReader<BlobPart> r, CancellationToken ct)
        {
            await foreach (var (_, data) in r.ReadAllAsync(ct)) {
                var appendCommand = new AppendTranscriptionCommand(transcriptId1, data);
                await Transcriber.AppendTranscription(appendCommand, ct);
            }

            await Transcriber.EndTranscription(new EndTranscriptionCommand(transcriptId1), ct);
        }
    }

    private async Task PublishChatEntry(
        AudioRecordSegment audioRecordSegment,
        CancellationToken cancellationToken)
    {
        var e = audioRecordSegment;
        var chatEntry = new ChatEntry(e.AudioRecord.ChatId, 0) {
            AuthorId = e.AudioRecord.UserId,
            Content = "...",
            ContentType = ChatContentType.Text,
            StreamId = e.StreamId
        };
        await Chat.CreateEntry( new ChatCommands.CreateEntry(chatEntry).MarkServerSide(), cancellationToken);
    }

    private async Task PublishAudioStream(AudioRecordSegment audioRecordSegment, CancellationToken cancellationToken)
        => await AudioStreamer.PublishAudioStream(audioRecordSegment.StreamId, await audioRecordSegment.GetAudioStream(), cancellationToken);
}
