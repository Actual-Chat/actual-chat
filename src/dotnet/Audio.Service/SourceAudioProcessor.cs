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
    public AudioSplitter AudioSplitter { get; }
    public AudioSourceStreamer AudioSourceStreamer { get; }
    public TranscriptSplitter TranscriptSplitter { get; }
    public TranscriptStreamer TranscriptStreamer { get; }
    public IChatsBackend ChatsBackend { get; }
    public MomentClockSet ClockSet { get; }

    public SourceAudioProcessor(
        Options settings,
        ITranscriber transcriber,
        AudioSegmentSaver audioSegmentSaver,
        SourceAudioRecorder sourceAudioRecorder,
        AudioSplitter audioSplitter,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptSplitter transcriptSplitter,
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
        AudioSplitter = audioSplitter;
        AudioSourceStreamer = audioSourceStreamer;
        TranscriptSplitter = transcriptSplitter;
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
        var blobStream = SourceAudioRecorder.GetSourceAudioBlobStream(record.Id, cancellationToken);
        if (Constants.DebugMode.AudioRecordingBlobStream)
            blobStream = blobStream.WithLog(Log, "ProcessSourceAudio", cancellationToken);
        var openSegments = AudioSplitter.GetSegments(record, blobStream, cancellationToken);
        await foreach (var openSegment in openSegments.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var beginsAt = ClockSet.CpuClock.UtcNow;
            DebugLog?.LogDebug(
                "ProcessSourceAudio: record #{RecordId} got segment #{SegmentIndex} w/ stream #{SegmentStreamId}",
                record.Id, openSegment.Index, openSegment.StreamId);
            var publishAudioTask = AudioSourceStreamer.Publish(openSegment.StreamId, openSegment.Audio, cancellationToken);
            var saveAudioTask = SaveAudio(openSegment, cancellationToken);
            var audioEntry = await CreateAudioEntry(openSegment, beginsAt, cancellationToken).ConfigureAwait(false);
            var transcribeTask = TranscribeAudio(openSegment, audioEntry, cancellationToken);

            _ = BackgroundTask.Run(FinalizeAudioProcessing,
                Log, $"{nameof(FinalizeAudioProcessing)} failed",
                CancellationToken.None);

            async Task FinalizeAudioProcessing()
            {
                // TODO(AY): We should make sure finalization happens no matter what (later)!
                await publishAudioTask.ConfigureAwait(false);
                var audioBlobId = await saveAudioTask.ConfigureAwait(false);
                await FinalizeAudioEntry(openSegment, audioEntry, audioBlobId, cancellationToken).ConfigureAwait(false);
                await transcribeTask.ConfigureAwait(false);
            }
        }
    }

    private async Task TranscribeAudio(OpenAudioSegment audioSegment, ChatEntry audioEntry, CancellationToken cancellationToken)
    {
        var audioStream = audioSegment.Audio.GetStream(cancellationToken);
        var transcriptionOptions = new TranscriptionOptions() {
            Language = "ru-RU",
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var allTranscripts = Transcriber.Transcribe(transcriptionOptions, audioStream, cancellationToken);
        var segments = TranscriptSplitter.GetSegments(audioSegment, allTranscripts, cancellationToken);
        var segmentTasks = new Queue<Task>();
        await foreach (var segment in segments.ConfigureAwait(false)) {
            var segmentTask = ProcessTranscriptSegment(audioSegment, audioEntry, segment, cancellationToken);
            segmentTasks.Enqueue(segmentTask);
            while (segmentTasks.Peek().IsCompleted)
                await segmentTasks.Dequeue().ConfigureAwait(false);
        }
        await Task.WhenAll(segmentTasks).ConfigureAwait(false);
    }

    private async Task ProcessTranscriptSegment(
        OpenAudioSegment audioSegment,
        ChatEntry audioEntry,
        TranscriptSegment segment,
        CancellationToken cancellationToken)
    {
        var streamId = $"{audioSegment.StreamId}-{segment.Index:D}";
        var transcripts = segment.Suffixes.Memoize(cancellationToken);
        var publishTask = TranscriptStreamer.Publish(streamId, transcripts.Replay(cancellationToken), cancellationToken);
        await CreateTextEntry(audioEntry, streamId, transcripts.Replay(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await publishTask.ConfigureAwait(false);
    }

    private async Task<string> SaveAudio(OpenAudioSegment openAudioSegment, CancellationToken cancellationToken)
    {
        var audioSegment = await openAudioSegment.ClosedSegmentTask.ConfigureAwait(false);
        return await AudioSegmentSaver.Save(audioSegment, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatEntry> CreateAudioEntry(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        CancellationToken cancellationToken)
    {
        var command = new IChatsBackend.UpsertEntryCommand(new ChatEntry() {
            ChatId = audioSegment.AudioRecord.ChatId,
            Type = ChatEntryType.Audio,
            AuthorId = audioSegment.AudioRecord.AuthorId,
            Content = "",
            StreamId = audioSegment.StreamId,
            BeginsAt = beginsAt,
        });
        var entry = await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("CreateAudioEntry: #{EntryId} is created in chat #{ChatId}", entry.Id, entry.ChatId);
        return entry;
    }

    private async Task FinalizeAudioEntry(
        OpenAudioSegment audioSegment,
        ChatEntry audioEntry,
        string? audioBlobId,
        CancellationToken cancellationToken)
    {
        var closedSegment = await audioSegment.ClosedSegmentTask.ConfigureAwait(false);
        audioEntry = audioEntry with {
            Content = audioBlobId ?? "",
            StreamId = Symbol.Empty,
            EndsAt = audioEntry.BeginsAt + closedSegment.Duration,
        };
        var command = new IChatsBackend.UpsertEntryCommand(audioEntry);
        await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateTextEntry(
        ChatEntry audioEntry,
        string transcriptStreamId,
        IAsyncEnumerable<Transcript> diffs,
        CancellationToken cancellationToken)
    {
        Transcript? transcript = null;
        ChatEntry? textEntry = null;
        await foreach (var diff in diffs.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (transcript != null) {
                transcript = transcript.WithDiff(diff);
                continue;
            }
            transcript = diff;
            textEntry = new ChatEntry() {
                ChatId = audioEntry.ChatId,
                Type = ChatEntryType.Text,
                AuthorId = audioEntry.AuthorId,
                Content = "",
                StreamId = transcriptStreamId,
                BeginsAt = audioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
            };
            textEntry = await ChatsBackend
                .UpsertEntry(new IChatsBackend.UpsertEntryCommand(textEntry), cancellationToken)
                .ConfigureAwait(false);
            DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}", textEntry.Id, textEntry.ChatId);
        }
        if (transcript == null)
            return;
        var textToTimeMap = transcript.TextToTimeMap.Move(-transcript.TextRange.Start, 0);
        textEntry = textEntry! with {
            Content = transcript.Text,
            StreamId = Symbol.Empty,
            AudioEntryId = audioEntry.Id,
            EndsAt = audioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.End),
            TextToTimeMap = textToTimeMap,
        };
        await ChatsBackend
            .UpsertEntry(new IChatsBackend.UpsertEntryCommand(textEntry), cancellationToken)
            .ConfigureAwait(false);
    }
}
