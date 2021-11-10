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
    public IChatsBackend ChatsBackend { get; }
    public MomentClockSet ClockSet { get; }

    public SourceAudioProcessor(
        ITranscriber transcriber,
        AudioSegmentSaver audioSegmentSaver,
        SourceAudioRecorder sourceAudioRecorder,
        AudioActivityExtractor audioActivityExtractor,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptStreamer transcriptStreamer,
        IChatsBackend chatsBackend,
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
        ChatsBackend = chatsBackend;
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
        var audioStream = SourceAudioRecorder.GetSourceAudioBlobStream(audioRecord.Id, cancellationToken);
        var openSegments = AudioActivityExtractor.SplitToAudioSegments(audioRecord, audioStream, cancellationToken);
        await foreach (var openSegment in openSegments.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var beginsAt = ClockSet.CpuClock.UtcNow;
            var publishAudioTask = AudioSourceStreamer.Publish(openSegment.StreamId, openSegment.Audio, cancellationToken);
            var publishTranscriptTask = PublishTranscriptStream(openSegment, cancellationToken);
            var saveAudioSegmentTask = SaveAudioSegment(openSegment, cancellationToken);
            var audioChatEntry = await CreateAudioChatEntry(openSegment, beginsAt, cancellationToken).ConfigureAwait(false);
            var textChatEntry =
                await CreateTextChatEntry(openSegment, audioChatEntry, cancellationToken).ConfigureAwait(false);

            _ = Task.Run(async () => {
                    // TODO(AY): We should make sure finalization happens no matter what (later)!
                    try { await publishAudioTask.ConfigureAwait(false); }
                    catch (Exception e) {
                        _log.LogError(e, "SourceAudioProcessor.PublishAudioStream(...) failed");
                    }

                    string? audioBlobId = null;
                    try { audioBlobId = await saveAudioSegmentTask.ConfigureAwait(false); }
                    catch (Exception e) {
                        _log.LogError(e, "SourceAudioProcessor.SaveAudioSegment(...) failed");
                    }

                    try {
                        await FinalizeAudioChatEntry(audioChatEntry, audioBlobId, openSegment, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        _log.LogError(e, "SourceAudioProcessor.FinalizeAudioChatEntry(...) failed");
                    }

                    Transcript? transcript = null;
                    try { transcript = await publishTranscriptTask.ConfigureAwait(false); }
                    catch (Exception e) {
                        _log.LogError(e, "SourceAudioProcessor.PublishTranscriptStream(...) failed");
                    }

                    try {
                        await FinalizeTextChatEntry(textChatEntry, transcript, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        _log.LogError(e, "SourceAudioProcessor.FinalizeTextChatEntry(...) failed");
                    }
                },
                cancellationToken);
        }
    }

    private async Task<string> SaveAudioSegment(OpenAudioSegment openAudioSegment, CancellationToken cancellationToken)
    {
        var audioSegment = await openAudioSegment.ClosedSegmentTask.ConfigureAwait(false);
        return await AudioSegmentSaver.Save(audioSegment, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Transcript> PublishTranscriptStream(
        OpenAudioSegment openSegment,
        CancellationToken cancellationToken)
    {
        // TODO(AK): read actual config
        var request = new TranscriptionRequest(
            openSegment.StreamId,
            new () {
                CodecKind = AudioCodecKind.Opus,
                ChannelCount = 1,
                SampleRate = 48_000,
            },
            new () {
                Language = "ru-RU",
                IsDiarizationEnabled = false,
                IsPunctuationEnabled = true,
                MaxSpeakerCount = 1,
            });

        var audioStream = openSegment.Audio.GetStream(cancellationToken);
        var transcriptStream = Transcriber.Transcribe(request, audioStream, cancellationToken);
        var memoizedTranscript = transcriptStream.Memoize();
        await TranscriptStreamer
            .Publish(openSegment.StreamId, memoizedTranscript.Replay(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        return await memoizedTranscript.Replay(cancellationToken)
            .GetTranscript(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChatEntry> CreateAudioChatEntry(
        OpenAudioSegment openSegment,
        Moment beginsAt,
        CancellationToken cancellationToken)
    {
        var chatEntry = new ChatEntry() {
            ChatId = openSegment.AudioRecord.ChatId,
            AuthorId = openSegment.AudioRecord.AuthorId,
            Content = "",
            Type = ChatEntryType.Audio,
            StreamId = openSegment.StreamId,
            BeginsAt = beginsAt,
        };
        var command = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatEntry> CreateTextChatEntry(
        OpenAudioSegment openSegment,
        ChatEntry audioChatEntry,
        CancellationToken cancellationToken)
    {
        var chatEntry = new ChatEntry() {
            ChatId = openSegment.AudioRecord.ChatId,
            AuthorId = openSegment.AudioRecord.AuthorId,
            Content = "...",
            Type = ChatEntryType.Text,
            StreamId = openSegment.StreamId,
            AudioEntryId = audioChatEntry.Id,
            BeginsAt = audioChatEntry.BeginsAt,
        };
        var command = new IChatsBackend.UpsertEntryCommand(chatEntry);
        return await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeAudioChatEntry(
        ChatEntry audioChatEntry,
        string? audioBlobId,
        OpenAudioSegment openSegment,
        CancellationToken cancellationToken)
    {
        var closedSegment = await openSegment.ClosedSegmentTask.ConfigureAwait(false);
        audioChatEntry = audioChatEntry with {
            Content = audioBlobId ?? "",
            StreamId = StreamId.None,
            EndsAt = audioChatEntry.BeginsAt.ToDateTime().Add(closedSegment.Duration),
        };
        var command = new IChatsBackend.UpsertEntryCommand(audioChatEntry);
        await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task FinalizeTextChatEntry(
        ChatEntry textChatEntry,
        Transcript? transcript,
        CancellationToken cancellationToken)
    {
        textChatEntry = transcript != null
            ? textChatEntry with {
                Content = transcript.Text,
                StreamId = StreamId.None,
                EndsAt = textChatEntry.BeginsAt.ToDateTime().AddSeconds(transcript.Duration),
                TextToTimeMap = transcript.TextToTimeMap,
            }
            : textChatEntry with {
                StreamId = StreamId.None,
                EndsAt = ClockSet.CpuClock.Now,
            };
        var command = new IChatsBackend.UpsertEntryCommand(textChatEntry);
        await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
    }
}
