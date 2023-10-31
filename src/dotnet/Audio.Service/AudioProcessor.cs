using System.Text.RegularExpressions;
using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.Audio;

public sealed partial class AudioProcessor : IAudioProcessor
{
    public record Options
    {
        public TimeSpan TranscriptDebouncePeriod { get; set; } = TimeSpan.FromSeconds(0.2);
        public bool IsEnabled { get; init; } = true;
    }

    [GeneratedRegex("^\\s*$")]
    private static partial Regex EmptyRegexFactory();

    private static readonly Regex EmptyRegex = EmptyRegexFactory();

    private ILogger Log { get; }
    private ILogger OpenAudioSegmentLog { get; }
    private ILogger AudioSourceLog { get; }
    private bool DebugMode => Constants.DebugMode.AudioProcessor;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private Options Settings { get; }
    private ITranscriber Transcriber { get; }
    private AudioSegmentSaver AudioSegmentSaver { get; }
    private IAudioStreamServer AudioStreamServer { get; }
    private ITranscriptStreamServer TranscriptStreamServer { get; }
    private IChats Chats { get; }
    private IAuthors Authors { get; }
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
        TranscriptStreamServer = services.GetRequiredService<ITranscriptStreamServer>();
        Chats = services.GetRequiredService<IChats>();
        Authors = services.GetRequiredService<IAuthors>();
        Accounts = services.GetRequiredService<IAccounts>();
        Commander = services.Commander();
        Clocks = services.Clocks();
        OpenAudioSegmentLog = services.LogFor<OpenAudioSegment>();
        AudioSourceLog = services.LogFor<AudioSource>();
        ServerKvas = services.GetRequiredService<IServerKvas>();
    }

    public async Task ProcessAudio(
        AudioRecord record,
        int preSkipFrames,
        IAsyncEnumerable<AudioFrame> recordingStream,
        CancellationToken cancellationToken)
    {
        Log.LogTrace(nameof(ProcessAudio) + ": record #{RecordId} = {Record}", record.Id, record);
        var beginsAt = Clocks.SystemClock.Now;
        string? streamId = null;
        try {
            var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken).ConfigureAwait(false);
            rules.Require(ChatPermissions.Write);

            if (Constants.DebugMode.AudioRecordingStream)
                recordingStream = recordingStream.WithLog(Log, nameof(ProcessAudio), cancellationToken);

            var language = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);
            var languages = ApiArray.New(language);

            var author = await Authors
                .EnsureJoined(record.Session, record.ChatId, cancellationToken)
                .ConfigureAwait(false);

            var transcriptionSettings = new VoiceSettings(Chats, Authors, ServerKvas.GetClient(record.Session));
            var voiceMode = await transcriptionSettings
                .GetVoiceMode(record.Session, record.ChatId, cancellationToken)
                .ConfigureAwait(false);

            var recordedAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);
            var audio = new AudioSource(
                new Moment(recordedAt),
                AudioSource.DefaultFormat with { PreSkipFrames = preSkipFrames },
                recordingStream,
                TimeSpan.Zero,
                AudioSourceLog,
                cancellationToken);
            var openSegment = new OpenAudioSegment(0,
                record,
                audio,
                author,
                languages,
                OpenAudioSegmentLog);
            openSegment.SetRecordedAt(recordedAt);
            streamId = openSegment.StreamId;

            var audioStream = openSegment.Audio
                .GetFrames(cancellationToken)
                .Select(f => f.Data)
                .Prepend(new ActualOpusStreamHeader(audio.CreatedAt, audio.Format).Serialize());
            var publishAudioTask = voiceMode.MustStreamVoice
                ? BackgroundTask.Run(
                    () => AudioStreamServer.Write(openSegment.StreamId, audioStream, cancellationToken),
                    Log,
                    $"{nameof(AudioStreamServer.Write)} failed",
                    cancellationToken)
                : Task.CompletedTask;

            var audioEntryTask = voiceMode.MustStreamVoice
                ? BackgroundTask.Run(
                    () => CreateAudioEntry(openSegment, beginsAt, recordedAt, cancellationToken),
                    Log,
                    $"{nameof(CreateAudioEntry)} failed",
                    cancellationToken)
                : Task.FromResult<ChatEntry?>(null);

            var transcribeTask = BackgroundTask.Run(
                () => TranscribeAudio(
                    openSegment,
                    beginsAt,
                    audioEntryTask,
                    voiceMode.MustStreamVoice,
                    CancellationToken.None),
                Log,
                $"{nameof(TranscribeAudio)} failed",
                CancellationToken.None);

            // TODO(AY): We should make sure finalization happens no matter what (later)!
            // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

            await Task.WhenAll(publishAudioTask, audioEntryTask).ConfigureAwait(false);
            await openSegment.Audio.WhenDurationAvailable.ConfigureAwait(false);
            // close open audio segment when the duration become available
            openSegment.Close(openSegment.Audio.Duration);
            var closedSegment = await openSegment.ClosedSegment.ConfigureAwait(false);
            // we don't use cancellationToken there because we should finalize audio entry
            // if it has been created successfully no matter of method cancellation
            var audioBlobId = voiceMode.MustStreamVoice
                ? await AudioSegmentSaver.Save(closedSegment, CancellationToken.None).ConfigureAwait(false)
                : null;
            // this should already have been completed by this time
            var audioEntry = await audioEntryTask.ConfigureAwait(false);
            if (audioEntry != null)
                await FinalizeAudioEntry(openSegment, audioEntry, audioBlobId, CancellationToken.None)
                    .ConfigureAwait(false);

            // we don't care much about transcription errors - basically we should finalize audio entry before
            await transcribeTask.ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error processing audio stream {StreamId}", streamId);
            throw;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Cancelled processing audio stream {StreamId}", streamId);
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

    private Task TranscribeAudio(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        Task<ChatEntry?> audioEntryTask,
        bool mustPersistAudio,
        CancellationToken cancellationToken)
    {
        var transcriptionOptions = new TranscriptionOptions {
            Language = audioSegment.Languages[0],
        };
        var transcripts = Transcriber
            .Transcribe(audioSegment.StreamId, audioSegment.Audio, transcriptionOptions, cancellationToken)
            .Throttle(Settings.TranscriptDebouncePeriod, Clocks.CpuClock, cancellationToken)
            .TrimOnCancellation(cancellationToken)
            .Memoize();
        cancellationToken = CancellationToken.None; // We already accounted for it in TrimOnCancellation

        var transcriptStreamId = audioSegment.StreamId;
        var publishTask = TranscriptStreamServer.Write(
            transcriptStreamId,
            transcripts.Replay(cancellationToken).ToTranscriptDiffs(),
            cancellationToken);
        var textEntryTask = CreateAndFinalizeTextEntry(
            audioSegment.AudioRecord.ChatId,
            audioSegment.Author.Id,
            beginsAt,
            audioEntryTask,
            transcriptStreamId,
            audioSegment.AudioRecord.RepliedChatEntryId,
            transcripts.Replay(cancellationToken),
            mustPersistAudio);
        return Task.WhenAll(publishTask, textEntryTask);
    }

    private async Task<ChatEntry?> CreateAudioEntry(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        Moment recordedAt,
        CancellationToken cancellationToken)
    {
        var delay = beginsAt - recordedAt;
        DebugLog?.LogDebug("CreateAudioEntry: delay={Delay:N1}ms", delay.TotalMilliseconds);

        var chatId = audioSegment.AudioRecord.ChatId;
        var entryId = new ChatEntryId(chatId, ChatEntryKind.Audio, 0, AssumeValid.Option);
        var command = new ChatsBackend_UpsertEntry(new ChatEntry(entryId) {
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
        var closedSegment = await audioSegment.ClosedSegment.ConfigureAwait(false);
        var endsAt = audioEntry.BeginsAt + closedSegment.Duration;
        var contentEndsAt = audioEntry.BeginsAt + closedSegment.AudibleDuration;
        contentEndsAt = Moment.Min(endsAt, contentEndsAt);
        audioEntry = audioEntry with {
            Content = audioBlobId ?? "",
            StreamId = Symbol.Empty,
            EndsAt = endsAt,
            ContentEndsAt = contentEndsAt,
        };
        var command = new ChatsBackend_UpsertEntry(audioEntry);
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateAndFinalizeTextEntry(
        ChatId chatId,
        AuthorId authorId,
        Moment beginsAt,
        Task<ChatEntry?> audioEntryTask,
        string transcriptStreamId,
        ChatEntryId repliedChatEntryId,
        IAsyncEnumerable<Transcript> transcripts,
        bool mustPersistAudio)
    {
        Transcript? lastTranscript = null;
        ChatEntry? textEntry = null;
        ChatsBackend_UpsertEntry? command;

        try {
            await foreach (var transcript in transcripts.ConfigureAwait(false)) {
                lastTranscript = transcript;
                if (textEntry != null)
                    continue;
                if (EmptyRegex.IsMatch(transcript.Text))
                    continue;

                // Got first non-empty transcript -> create text entry
                var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0, AssumeValid.Option);
                textEntry = new ChatEntry(entryId) {
                    AuthorId = authorId,
                    Content = "",
                    StreamId = transcriptStreamId,
                    BeginsAt = beginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
                    RepliedEntryLocalId = repliedChatEntryId is { IsNone: false, LocalId: var localId }
                        ? localId
                        : null,
                };
                command = new ChatsBackend_UpsertEntry(textEntry);
                textEntry = await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
                DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}",
                    textEntry.Id,
                    textEntry.ChatId);
            }
        }
        finally {
            if (lastTranscript != null && textEntry != null) {
                var chatAudioEntry = await audioEntryTask.ConfigureAwait(false);
                var timeMap = lastTranscript.TimeMap.Move(-lastTranscript.TextRange.Start, 0);
                textEntry = textEntry with {
                    Content = lastTranscript.Text,
                    StreamId = Symbol.Empty,
                    AudioEntryId = mustPersistAudio ? chatAudioEntry?.LocalId : null,
                    EndsAt = beginsAt + TimeSpan.FromSeconds(lastTranscript.TimeRange.End),
                    TimeMap = mustPersistAudio ? timeMap : default,
                };
                if (EmptyRegex.IsMatch(textEntry.Content)) {
                    // Final transcript is empty -> remove text entry
                    // TODO(AY): Maybe publish [Audio: ...] markup here
                    textEntry = textEntry with { IsRemoved = true };
                }
                command = new ChatsBackend_UpsertEntry(textEntry);
                await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
            }
            // TODO(AY): Maybe publish [Audio: ...] markup here
        }
    }
}
