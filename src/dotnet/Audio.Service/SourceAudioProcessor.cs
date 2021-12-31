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
    public TranscriptPostProcessor TranscriptPostProcessor { get; }
    public TranscriptStreamer TranscriptStreamer { get; }
    public IChatsBackend ChatsBackend { get; }
    public MomentClockSet Clocks { get; }

    public SourceAudioProcessor(
        Options settings,
        ITranscriber transcriber,
        AudioSegmentSaver audioSegmentSaver,
        SourceAudioRecorder sourceAudioRecorder,
        AudioSplitter audioSplitter,
        AudioSourceStreamer audioSourceStreamer,
        TranscriptSplitter transcriptSplitter,
        TranscriptPostProcessor transcriptPostProcessor,
        TranscriptStreamer transcriptStreamer,
        IChatsBackend chatsBackend,
        MomentClockSet clocks,
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
        TranscriptPostProcessor = transcriptPostProcessor;
        TranscriptStreamer = transcriptStreamer;
        ChatsBackend = chatsBackend;
        Clocks = clocks;
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
        var recordingStream = SourceAudioRecorder.GetSourceAudioRecordingStream(record.Id, cancellationToken);
        if (Constants.DebugMode.AudioRecordingStream)
            recordingStream = recordingStream.WithLog(Log, "ProcessSourceAudio", cancellationToken);
        var openSegments = AudioSplitter.GetSegments(record, recordingStream, cancellationToken);
        await foreach (var openSegment in openSegments.ConfigureAwait(false)) {
            DebugLog?.LogDebug(
                "ProcessSourceAudio: record #{RecordId} got segment #{SegmentIndex} w/ stream #{SegmentStreamId}",
                record.Id, openSegment.Index, openSegment.StreamId);
            var publishAudioTask = AudioSourceStreamer.Publish(openSegment.StreamId, openSegment.Audio, cancellationToken);
            var saveAudioTask = SaveAudio(openSegment, cancellationToken);
            var audioEntryTask = CreateAudioEntry(openSegment, cancellationToken);
            var transcribeTask = TranscribeAudio(openSegment, audioEntryTask, cancellationToken);

            _ = BackgroundTask.Run(FinalizeAudioProcessing,
                Log, $"{nameof(FinalizeAudioProcessing)} failed",
                CancellationToken.None);

            async Task FinalizeAudioProcessing()
            {
                // TODO(AY): We should make sure finalization happens no matter what (later)!
                await publishAudioTask.ConfigureAwait(false);
                var audioEntry = await audioEntryTask.ConfigureAwait(false);
                var audioBlobId = await saveAudioTask.ConfigureAwait(false);
                await FinalizeAudioEntry(openSegment, audioEntry, audioBlobId, cancellationToken).ConfigureAwait(false);
                await transcribeTask.ConfigureAwait(false);
            }
        }
    }

    private async Task TranscribeAudio(OpenAudioSegment audioSegment, Task<ChatEntry> audioEntryTask, CancellationToken cancellationToken)
    {
        var transcriptionOptions = new TranscriptionOptions() {
            Language = audioSegment.AudioRecord.Language,
            AltLanguages = Array.Empty<string>(),
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var allTranscripts = Transcriber.Transcribe(transcriptionOptions, audioSegment.Audio, cancellationToken);
        var segments = TranscriptSplitter.GetSegments(audioSegment, allTranscripts, cancellationToken);
        var segmentTasks = new Queue<Task>();
        await foreach (var segment in segments.ConfigureAwait(false)) {
            var segmentTask = ProcessTranscriptSegment(audioSegment, audioEntryTask, segment, cancellationToken);
            segmentTasks.Enqueue(segmentTask);
            while (segmentTasks.Peek().IsCompleted)
                await segmentTasks.Dequeue().ConfigureAwait(false);
        }
        await Task.WhenAll(segmentTasks).ConfigureAwait(false);
    }

    private async Task ProcessTranscriptSegment(
        OpenAudioSegment audioSegment,
        Task<ChatEntry> audioEntryTask,
        TranscriptSegment segment,
        CancellationToken cancellationToken)
    {
        var streamId = $"{audioSegment.StreamId}-{segment.Index:D}";
        var transcripts = TranscriptPostProcessor.Apply(segment, cancellationToken);
        var diffs = transcripts.GetDiffs(cancellationToken).Memoize(); // Should
        var publishTask = TranscriptStreamer.Publish(streamId, diffs.Replay(cancellationToken), cancellationToken);
        var audioEntry = await audioEntryTask.ConfigureAwait(false);
        await CreateTextEntry(audioEntry, streamId, diffs.Replay(cancellationToken), cancellationToken)
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
        CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        DebugLog?.LogDebug("CreateAudioEntry: started, waiting for RecordedAt");
        var beginsAt = now;
        var recordedAtOpt = await audioSegment.RecordedAtTask
            .WithTimeout(TimeSpan.FromSeconds(0.1), cancellationToken) // Any delay here contribs to the overall delay
            .ConfigureAwait(false);
        var recordedAt = (recordedAtOpt.IsSome(out var v) ? v : null) ?? beginsAt;
        if (recordedAt + TimeSpan.FromSeconds(3) >= beginsAt) // We're ok with max. 3s delta
            beginsAt = Moment.Min(beginsAt, recordedAt);
        var delay = now - recordedAt;
        DebugLog?.LogDebug("CreateAudioEntry: delay={Delay:N1}ms", delay.TotalMilliseconds);

        var command = new IChatsBackend.UpsertEntryCommand(new ChatEntry() {
            ChatId = audioSegment.AudioRecord.ChatId,
            Type = ChatEntryType.Audio,
            AuthorId = audioSegment.AudioRecord.AuthorId,
            Content = "",
            StreamId = audioSegment.StreamId,
            BeginsAt = beginsAt,
            ClientSideBeginsAt = recordedAt,
        });
        var audioEntry = await ChatsBackend.UpsertEntry(command, cancellationToken).ConfigureAwait(false);
        return audioEntry;
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
            ContentEndsAt = audioEntry.BeginsAt + closedSegment.AudibleDuration,
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
