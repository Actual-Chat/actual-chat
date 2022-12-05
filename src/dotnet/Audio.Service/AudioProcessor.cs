using System.Text.RegularExpressions;
using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.Audio;

public sealed class AudioProcessor : IAudioProcessor
{
    public record Options
    {
        public bool IsEnabled { get; init; } = true;
    }

    private static readonly Regex EmptyRegex = new ("^\\s*$", RegexOptions.Compiled);

    private ILogger Log { get; }
    private ILogger OpenAudioSegmentLog { get; }
    private ILogger AudioSourceLog { get; }
    private bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private Options Settings { get; }
    private ITranscriber Transcriber { get; }
    private AudioSegmentSaver AudioSegmentSaver { get; }
    private IAudioStreamServer AudioStreamServer { get; }
    private TranscriptSplitter TranscriptSplitter { get; }
    private TranscriptPostProcessor TranscriptPostProcessor { get; }
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private IChats Chats { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IAccounts Accounts { get; }
    private ICommander Commander { get; }
    private MomentClockSet Clocks { get; }
    private IServerKvas ServerKvas { get; }

    public AudioProcessor(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Log = services.LogFor(GetType());
        Transcriber = services.GetRequiredService<ITranscriber>();
        AudioSegmentSaver = services.GetRequiredService<AudioSegmentSaver>();
        AudioStreamServer = services.GetRequiredService<IAudioStreamServer>();
        TranscriptSplitter = services.GetRequiredService<TranscriptSplitter>();
        TranscriptPostProcessor = services.GetRequiredService<TranscriptPostProcessor>();
        TranscriptStreamServer = services.GetRequiredService<ITranscriptStreamServer>();
        Chats = services.GetRequiredService<IChats>();
        Accounts = services.GetRequiredService<IAccounts>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        Commander = services.Commander();
        Clocks = services.Clocks();
        OpenAudioSegmentLog = services.LogFor<OpenAudioSegment>();
        AudioSourceLog = services.LogFor<AudioSource>();
        ServerKvas = services.GetRequiredService<IServerKvas>();
    }

    public async Task ProcessAudio(
        AudioRecord record,
        IAsyncEnumerable<AudioFrame> recordingStream,
        CancellationToken cancellationToken)
    {
        Log.LogTrace(nameof(ProcessAudio) + ": record #{RecordId} = {Record}", record.Id, record);

        string? streamId = null;
        try {
            var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken).ConfigureAwait(false);
            rules.Require(ChatPermissions.Write);

            if (Constants.DebugMode.AudioRecordingStream)
                recordingStream = recordingStream.WithLog(Log, nameof(ProcessAudio), cancellationToken);

            var language = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);
            var languages = ImmutableArray.Create(language);

            var author = await AuthorsBackend.GetOrCreate(record.Session, record.ChatId, cancellationToken)
                .ConfigureAwait(false);
            var audio = new AudioSource(
                Task.FromResult(AudioSource.DefaultFormat),
                recordingStream,
                TimeSpan.Zero,
                AudioSourceLog,
                cancellationToken);
            var openSegment = new OpenAudioSegment(
                0,
                record,
                audio,
                author,
                languages,
                OpenAudioSegmentLog);
            streamId = openSegment.StreamId;
            openSegment.SetRecordedAt(Moment.EpochStart + TimeSpan.FromSeconds(record.ClientStartOffset));

            var audioStream = openSegment.Audio
                .GetFrames(cancellationToken)
                .Select(f => f.Data);

            var publishAudioTask = BackgroundTask.Run(
                () => AudioStreamServer.Write(openSegment.StreamId, audioStream, cancellationToken),
                Log,
                $"{nameof(AudioStreamServer.Write)} failed",
                cancellationToken);

            var audioEntryTask = BackgroundTask.Run(
                () => CreateAudioEntry(openSegment, cancellationToken),
                Log,
                $"{nameof(CreateAudioEntry)} failed",
                cancellationToken);

            var transcribeTask = BackgroundTask.Run(
                () => TranscribeAudio(record.ChatId, openSegment, audioEntryTask, cancellationToken),
                Log,
                $"{nameof(TranscribeAudio)} failed",
                CancellationToken.None);

            _ = BackgroundTask.Run(FinalizeAudioProcessing,
                Log,
                $"{nameof(FinalizeAudioProcessing)} failed",
                CancellationToken.None);

            await openSegment.Audio.WhenDurationAvailable.ConfigureAwait(false);

            async Task FinalizeAudioProcessing()
            {
                // TODO(AY): We should make sure finalization happens no matter what (later)!
                // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)
                // real-time audio transmission has been completed
                await publishAudioTask.ConfigureAwait(false);
                // this should already have been completed by this time
                var audioEntry = await audioEntryTask.ConfigureAwait(false);
                // close open audio segment when the duration become available
                await openSegment.Audio.WhenDurationAvailable.ConfigureAwait(false);
                openSegment.Close(openSegment.Audio.Duration);
                var closedSegment = await openSegment.ClosedSegmentTask.ConfigureAwait(false);
                // we don't use cancellationToken there because we should finalize audio entry
                // if it has been created successfully no matter of method cancellation
                var audioBlobId = await AudioSegmentSaver.Save(closedSegment, CancellationToken.None)
                    .ConfigureAwait(false);
                await FinalizeAudioEntry(openSegment, audioEntry, audioBlobId, CancellationToken.None)
                    .ConfigureAwait(false);

                // we don't care much about transcription errors - basically we should finalize audio entry before
                await transcribeTask.ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error processing audio stream {StreamId}", streamId);
            throw;
        }
    }

    // Private methods

    private async Task<Language> GetTranscriptionLanguage(AudioRecord record, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(record.Session);
        var userChatSettings = await kvas.GetUserChatSettings(record.ChatId, cancellationToken).ConfigureAwait(false);
        var language = await userChatSettings.LanguageOrPrimary(kvas, cancellationToken).ConfigureAwait(false);
        return language;
    }

    private async Task TranscribeAudio(
        Symbol identity,
        OpenAudioSegment audioSegment,
        Task<ChatEntry> audioEntryTask,
        CancellationToken cancellationToken)
    {
        var transcriptionOptions = new TranscriptionOptions {
            Language = audioSegment.Languages[0],
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var allTranscripts = Transcriber.Transcribe(
            identity,
            transcriptionOptions,
            audioSegment.Audio,
            cancellationToken);
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
        // TODO(AY): review cancellation stuff
        var diffs = transcripts.GetDiffs(cancellationToken).Memoize(cancellationToken);
        var publishTask = TranscriptStreamServer.Write(
            streamId,
            diffs.Replay(cancellationToken),
            cancellationToken);
        var textEntryTask = CreateAndFinalizeTextEntry(
            audioEntryTask,
            streamId,
            diffs.Replay(cancellationToken),
            cancellationToken);
        await Task.WhenAll(publishTask, textEntryTask).ConfigureAwait(false);
    }

    private async Task<ChatEntry> CreateAudioEntry(
        OpenAudioSegment audioSegment,
        CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        var beginsAt = now;
        var recordedAt = beginsAt;
        try {
            DebugLog?.LogDebug("CreateAudioEntry: started, waiting for RecordedAt");
            recordedAt = await audioSegment.RecordedAtTask
                    .WaitAsync(TimeSpan.FromMilliseconds(25), cancellationToken)
                    .ConfigureAwait(false)
                ?? recordedAt;
        }
        catch (TimeoutException) {
            // In case of delay recordedAt = beginsAt.
            // Any delay here contributes to the overall delay,
            // so we don't want to wait for too long for RecordedAtTask.
        }

        var delay = now - recordedAt;
        DebugLog?.LogDebug("CreateAudioEntry: delay={Delay:N1}ms", delay.TotalMilliseconds);

        var chatId = audioSegment.AudioRecord.ChatId;
        var entryId = new ChatEntryId(chatId, ChatEntryKind.Audio, 0, AssumeValid.Option);
        var command = new IChatsBackend.UpsertEntryCommand(new ChatEntry(entryId) {
            AuthorId = audioSegment.Author.Id,
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
        ChatEntry? chatAudioEntry = null;
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
            chatAudioEntry ??= await audioEntryTask.ConfigureAwait(false);
            var entryId = new ChatEntryId(chatAudioEntry.ChatId, ChatEntryKind.Text, 0, AssumeValid.Option);
            textEntry = new ChatEntry(entryId) {
                AuthorId = chatAudioEntry.AuthorId,
                Content = "",
                StreamId = transcriptStreamId,
                BeginsAt = chatAudioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
            };
            command = new IChatsBackend.UpsertEntryCommand(textEntry);
            textEntry = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}",
                textEntry.Id,
                textEntry.ChatId);
        }
        if (transcript == null || textEntry == null)
            // TODO(AY): Maybe publish [Audio: ...] markup here
            return;

        var textToTimeMap = transcript.TextToTimeMap.Move(-transcript.TextRange.Start, 0);
        textEntry = textEntry with {
            Content = transcript.Text,
            StreamId = Symbol.Empty,
            AudioEntryId = chatAudioEntry!.LocalId,
            EndsAt = chatAudioEntry.BeginsAt + TimeSpan.FromSeconds(transcript.TimeRange.End),
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
