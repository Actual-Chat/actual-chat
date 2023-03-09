using System.Text.RegularExpressions;
using ActualChat.Audio.Processing;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Transcription;
using ActualChat.Users;
using Cysharp.Text;

namespace ActualChat.Audio;

public sealed class AudioProcessor : IAudioProcessor
{
    public record Options
    {
        public TimeSpan TranscriptDebouncePeriod { get; set; } = TimeSpan.FromSeconds(0.2);
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

        string? streamId = null;
        try {
            var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken).ConfigureAwait(false);
            rules.Require(ChatPermissions.Write);

            if (Constants.DebugMode.AudioRecordingStream)
                recordingStream = recordingStream.WithLog(Log, nameof(ProcessAudio), cancellationToken);

            var language = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);
            var languages = ImmutableArray.Create(language);

            var author = await Authors
                .EnsureJoined(record.Session, record.ChatId, cancellationToken)
                .ConfigureAwait(false);
            var recordedAt = Moment.EpochStart + TimeSpan.FromSeconds(record.ClientStartOffset);
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
            var publishAudioTask = BackgroundTask.Run(
                () => AudioStreamServer.Write(openSegment.StreamId, audioStream, cancellationToken),
                Log,
                $"{nameof(AudioStreamServer.Write)} failed",
                cancellationToken);

            var audioEntryTask = BackgroundTask.Run(
                () => CreateAudioEntry(openSegment, recordedAt, cancellationToken),
                Log,
                $"{nameof(CreateAudioEntry)} failed",
                cancellationToken);

            var transcribeTask = BackgroundTask.Run(
                () => TranscribeAudio(openSegment, audioEntryTask, CancellationToken.None),
                Log,
                $"{nameof(TranscribeAudio)} failed",
                CancellationToken.None);

            // TODO(AY): We should make sure finalization happens no matter what (later)!
            // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

            await publishAudioTask.ConfigureAwait(false);
            await audioEntryTask.ConfigureAwait(false);
            await openSegment.Audio.WhenDurationAvailable.ConfigureAwait(false);
            // close open audio segment when the duration become available
            openSegment.Close(openSegment.Audio.Duration);
            var closedSegment = await openSegment.ClosedSegmentTask.ConfigureAwait(false);
            // we don't use cancellationToken there because we should finalize audio entry
            // if it has been created successfully no matter of method cancellation
            var audioBlobId = await AudioSegmentSaver.Save(closedSegment, CancellationToken.None)
                .ConfigureAwait(false);
            // this should already have been completed by this time
            var audioEntry = await audioEntryTask.ConfigureAwait(false);
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

    private async Task TranscribeAudio(
        OpenAudioSegment audioSegment,
        Task<ChatEntry> audioEntryTask,
        CancellationToken cancellationToken)
    {
        var transcriptionOptions = new TranscriptionOptions {
            Language = audioSegment.Languages[0],
        };
        var transcripts = Transcriber
            .Transcribe(audioSegment.StreamId, audioSegment.Audio, transcriptionOptions, cancellationToken)
            .Throttle(Settings.TranscriptDebouncePeriod, Clocks.CpuClock, cancellationToken);
        var memoizedTranscripts = PostProcessTranscript(transcripts).Memoize();

        var transcriptStreamId = audioSegment.StreamId;
        var publishTask = TranscriptStreamServer.Write(
            transcriptStreamId,
            memoizedTranscripts.Replay(cancellationToken).ToTranscriptDiffs(cancellationToken),
            cancellationToken);
        var textEntryTask = CreateAndFinalizeTextEntry(
            audioEntryTask,
            transcriptStreamId,
            memoizedTranscripts.Replay(cancellationToken),
            cancellationToken);
        await Task.WhenAll(publishTask, textEntryTask).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<Transcript> PostProcessTranscript(IAsyncEnumerable<Transcript> transcripts)
    {
        await foreach (var transcript in transcripts.ConfigureAwait(false)) {
            var text = transcript.Text;
            var contentStart = transcript.GetContentStart() - transcript.TextRange.Start;
            if (contentStart == text.Length) {
                yield return transcript;
                continue;
            }

            var firstLetter = text[contentStart];
            var firstLetterUpper = char.ToUpperInvariant(firstLetter);
            if (firstLetter == firstLetterUpper) {
                yield return transcript;
                continue;
            }

            var newText = ZString.Concat(text[..contentStart], firstLetterUpper, text[(contentStart + 1)..]);
            var newTranscript = transcript with { Text = newText };
            yield return newTranscript;
        }
    }

    private async Task<ChatEntry> CreateAudioEntry(
        OpenAudioSegment audioSegment,
        Moment recordedAt,
        CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now;
        var delay = now - recordedAt;
        DebugLog?.LogDebug("CreateAudioEntry: delay={Delay:N1}ms", delay.TotalMilliseconds);

        var chatId = audioSegment.AudioRecord.ChatId;
        var entryId = new ChatEntryId(chatId, ChatEntryKind.Audio, 0, AssumeValid.Option);
        var command = new IChatsBackend.UpsertEntryCommand(new ChatEntry(entryId) {
            AuthorId = audioSegment.Author.Id,
            Content = "",
            StreamId = audioSegment.StreamId,
            BeginsAt = now,
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
        Transcript? lastTranscript = null;
        ChatEntry? chatAudioEntry = null;
        ChatEntry? textEntry = null;
        IChatsBackend.UpsertEntryCommand? command;

        try {
            await foreach (var transcript in diffs.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                lastTranscript = transcript;
                if (textEntry != null)
                    continue;
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
        }
        finally {
            if (lastTranscript != null && textEntry != null) {
                var textToTimeMap = lastTranscript.TimeMap.Move(-lastTranscript.TextRange.Start, 0);
                textEntry = textEntry with {
                    Content = lastTranscript.Text,
                    StreamId = Symbol.Empty,
                    AudioEntryId = chatAudioEntry!.LocalId,
                    EndsAt = chatAudioEntry.BeginsAt + TimeSpan.FromSeconds(lastTranscript.TimeRange.End),
                    TextToTimeMap = textToTimeMap,
                };
                if (EmptyRegex.IsMatch(textEntry.Content)) {
                    // Final transcript is empty -> remove text entry
                    // TODO(AY): Maybe publish [Audio: ...] markup here
                    textEntry = textEntry with { IsRemoved = true };
                }
                command = new IChatsBackend.UpsertEntryCommand(textEntry);
                await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
            }
            // TODO(AY): Maybe publish [Audio: ...] markup here
        }
    }
}
