using System.Text.RegularExpressions;
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

    private static readonly Regex EmptyRegex = new("^\\s*$", RegexOptions.Compiled);

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
    public ICommander Commander { get; }
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
        ICommander commander,
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
        Commander = commander;
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
                    e => Log.LogError(e, "Failed to process AudioRecord={Record}", record),
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
            var transcribeTask = BackgroundTask.Run(
                () => TranscribeAudio(openSegment, audioEntryTask, cancellationToken),
                Log, $"{nameof(TranscribeAudio)} failed",
                CancellationToken.None);

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
        var transcripts = TranscriptPostProcessor
            .Apply(segment, cancellationToken)
            .TrimOnCancellation(cancellationToken);

        // Cancellation is "embedded" into transcripts at this point, so...
        cancellationToken = CancellationToken.None;
        var diffs = transcripts.GetDiffs(cancellationToken).Memoize();
        var publishTask = TranscriptStreamer.Publish(streamId, diffs.Replay(cancellationToken), cancellationToken);
        var textEntryTask = CreateAndFinalizeTextEntry(audioEntryTask, streamId, diffs.Replay(cancellationToken), cancellationToken);
        await Task.WhenAll(publishTask, textEntryTask).ConfigureAwait(false);
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
        // Any delay here contributes to the overall delay,
        // so we don't want to wait for too long for RecordedAtTask
        var recordedAtOpt = await audioSegment.RecordedAtTask
            .WithTimeout(TimeSpan.FromMilliseconds(25), cancellationToken)
            .ConfigureAwait(false);
        var recordedAt = (recordedAtOpt.IsSome(out var v) ? v : null) ?? beginsAt;
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
        var audioEntry = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return audioEntry;
    }

    private async Task FinalizeAudioEntry(
        OpenAudioSegment audioSegment,
        ChatEntry audioEntry,
        string? audioBlobId,
        CancellationToken cancellationToken)
    {
        var closedSegment = await audioSegment.ClosedSegmentTask.ConfigureAwait(false);
        var endsAt = audioEntry.BeginsAt + closedSegment.Duration;
        var contentEndsAt = audioEntry.BeginsAt + closedSegment.AudibleDuration;
        contentEndsAt = Moment.Min(endsAt, contentEndsAt);
        audioEntry = audioEntry with {
            Content = audioBlobId ?? "",
            StreamId = Symbol.Empty,
            EndsAt = endsAt,
            ContentEndsAt = contentEndsAt,
        };
        var command = new IChatsBackend.UpsertEntryCommand(audioEntry);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateAndFinalizeTextEntry(
        Task<ChatEntry> audioEntryTask,
        string transcriptStreamId,
        IAsyncEnumerable<Transcript> diffs,
        CancellationToken cancellationToken)
    {
        Transcript? transcript = null;
        ChatEntry? audioEntry = null;
        ChatEntry? textEntry = null;
        IChatsBackend.UpsertEntryCommand? command;

        await foreach (var diff in diffs.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (transcript != null) {
                transcript = transcript.WithDiff(diff);
                if (textEntry != null)
                    continue;
            }
            transcript = diff;
            if (EmptyRegex.IsMatch(transcript.Text))
                continue;

            // Got first non-empty transcript -> create text entry
            audioEntry ??= await audioEntryTask.ConfigureAwait(false);
            textEntry = new ChatEntry() {
                ChatId = audioEntry.ChatId,
                Type = ChatEntryType.Text,
                AuthorId = audioEntry.AuthorId,
                Content = "",
                StreamId = transcriptStreamId,
                BeginsAt = audioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
            };
            command = new IChatsBackend.UpsertEntryCommand(textEntry);
            textEntry = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}", textEntry.Id, textEntry.ChatId);
        }
        if (transcript == null || textEntry == null)
            // TODO(AY): Maybe publish [Audio: ...] markup here
            return;

        var textToTimeMap = transcript.TextToTimeMap.Move(-transcript.TextRange.Start, 0);
        textEntry = textEntry with {
            Content = transcript.Text,
            StreamId = Symbol.Empty,
            AudioEntryId = audioEntry!.Id,
            EndsAt = audioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.End),
            TextToTimeMap = textToTimeMap,
        };
        if (EmptyRegex.IsMatch(transcript.Text)) {
            // Final transcript is empty -> remove text entry
            // TODO(AY): Maybe publish [Audio: ...] markup here
            textEntry = textEntry with { IsRemoved = true };
        }
        command = new IChatsBackend.UpsertEntryCommand(textEntry);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
}
