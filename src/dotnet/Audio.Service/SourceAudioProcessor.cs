using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Transcription;

namespace ActualChat.Audio;

public class SourceAudioProcessor : AsyncProcessBase
{
    public record Options
    {
        public bool IsEnabled { get; init; } = true;
    }

    protected ILogger<SourceAudioProcessor> Log { get; }
    protected bool DebugMode => Constants.DebugMode.AudioProcessing;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    public Options Settings { get; }
    public ITranscriber Transcriber { get; }
    public AudioSegmentSaver AudioSegmentSaver { get; }
    public SourceAudioRecorder SourceAudioRecorder { get; }
    public AudioActivityExtractor AudioActivityExtractor { get; }
    public AudioSourceStreamer AudioSourceStreamer { get; }
    public TranscriptStreamer TranscriptStreamer { get; }
    public IChatsBackend ChatsBackend { get; }
    public MomentClockSet ClockSet { get; }

    public SourceAudioProcessor(
        Options settings,
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
        Log = log;
        Settings = settings;
        Transcriber = transcriber;
        AudioSegmentSaver = audioSegmentSaver;
        SourceAudioRecorder = sourceAudioRecorder;
        AudioActivityExtractor = audioActivityExtractor;
        AudioSourceStreamer = audioSourceStreamer;
        TranscriptStreamer = transcriptStreamer;
        ChatsBackend = chatsBackend;
        ClockSet = clockSet;
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        if (!Settings.IsEnabled)
            return;

        // TODO(AK): add push-back based on current node performance metrics \ or provide signals for scale-out
        while (true) {
            try {
                var record = await SourceAudioRecorder.DequeueSourceAudio(cancellationToken).ConfigureAwait(false);
                _ = BackgroundTask.Run(
                    () => ProcessSourceAudio(record, cancellationToken),
                    e => Log.LogError(e, "Failed to process AudioRecord: {Record}", record),
                    cancellationToken);
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "DequeueSourceAudio failed");
            }
        }
    }

    internal async Task ProcessSourceAudio(AudioRecord record, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("ProcessSourceAudio: record #{RecordId} = {Record}", record.Id, record);
        var stream = SourceAudioRecorder.GetSourceAudioBlobStream(record.Id, cancellationToken);
        if (Constants.DebugMode.AudioRecordingBlobStream)
            stream = stream.WithLog(Log, "ProcessSourceAudio", cancellationToken);
        var openSegments = AudioActivityExtractor.SplitToAudioSegments(record, stream, cancellationToken);
        await foreach (var openSegment in openSegments.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var beginsAt = ClockSet.CpuClock.UtcNow;
            DebugLog?.LogDebug(
                "ProcessSourceAudio: record #{RecordId} got segment #{SegmentIndex} w/ stream #{SegmentStreamId}",
                record.Id, openSegment.Index, openSegment.StreamId);
            var publishAudioTask = AudioSourceStreamer.Publish(openSegment.StreamId, openSegment.Audio, cancellationToken);
            var publishTranscriptTask = PublishTranscriptStream(openSegment, cancellationToken);
            var saveAudioSegmentTask = SaveAudioSegment(openSegment, cancellationToken);
            DebugLog?.LogDebug(
                "ProcessSourceAudio: record #{RecordId}, segment #{SegmentIndex}: starting to create chat entries",
                record.Id, openSegment.Index);
            var (audioChatEntry, textChatEntry) = await CreateChatEntries(openSegment, beginsAt, cancellationToken)
                .ConfigureAwait(false);

            _ = BackgroundTask.Run(FinalizeAudioProcessing,
                Log, $"{nameof(FinalizeAudioProcessing)} failed",
                CancellationToken.None);

            async Task FinalizeAudioProcessing()
            {
                // TODO(AY): We should make sure finalization happens no matter what (later)!
                await publishAudioTask.ConfigureAwait(false);
                var audioBlobId = await saveAudioSegmentTask.ConfigureAwait(false);
                await FinalizeAudioChatEntry(audioChatEntry, audioBlobId, openSegment, cancellationToken)
                    .ConfigureAwait(false);
                var transcript = await publishTranscriptTask.ConfigureAwait(false);
                await FinalizeTextChatEntry(textChatEntry, transcript, cancellationToken)
                    .ConfigureAwait(false);
            }
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

    private async Task<(ChatEntry AudioEntry, ChatEntry TextEntry)> CreateChatEntries(
        OpenAudioSegment openSegment,
        Moment beginsAt,
        CancellationToken cancellationToken)
    {
        var command = new IChatsBackend.CreateAudioEntryCommand(new ChatEntry() {
            ChatId = openSegment.AudioRecord.ChatId,
            AuthorId = openSegment.AudioRecord.AuthorId,
            Content = "",
            Type = ChatEntryType.Audio,
            StreamId = openSegment.StreamId,
            BeginsAt = beginsAt,
        });
        var entries = await ChatsBackend.CreateAudioEntry(command, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug(
            "CreateChatEntries: #{AudioEntryId} + #{TextEntryId} are created in chat #{ChatId}",
            entries.AudioEntry.Id, entries.TextEntry.Id, entries.AudioEntry.ChatId);
        return entries;
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
