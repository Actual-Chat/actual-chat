using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class StreamingBackend
{
    [GeneratedRegex("^\\s*$")]
    private static partial Regex EmptyRegexFactory();
    private static readonly Regex EmptyRegex = EmptyRegexFactory();

    public virtual async Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        RpcStream<AudioFrame> frames,
        CancellationToken cancellationToken)
    {
        ValidateStreamId(record.StreamId);
        Log.LogTrace(nameof(ProcessAudio) + ": record #{StreamId} = {Record}", record.StreamId, record);
        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Transcription.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;
        try {
            var augmentedFrames = frames.AsAsyncEnumerable();
            if (Constants.DebugMode.AudioRecordingStream)
                augmentedFrames = augmentedFrames.WithLog(Log, nameof(ProcessAudio), cancellationToken);
            await ProcessAudio(record, preSkip, augmentedFrames, delayedCancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error processing audio stream {StreamId}", record.StreamId);
            throw;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Cancelled processing audio stream {StreamId}", record.StreamId);
            throw;
        }
        finally {
            delayedCts.CancelAndDisposeSilently();
        }
    }

    // Private methods

    public async Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        IAsyncEnumerable<AudioFrame> frames,
        CancellationToken cancellationToken)
    {
        var beginsAt = Clocks.SystemClock.Now;
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var languages = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        var accountSettings = new AccountSettings(ServerKvas, record.Session);
        var chatVoiceMode = await accountSettings
            .GetChatVoiceMode(record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        var mustStreamVoice = chatVoiceMode.VoiceMode.HasVoice();

        var recordedAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);
        var audio = new AudioSource(
            new Moment(recordedAt),
            AudioSource.DefaultFormat with { PreSkip = preSkip },
            frames,
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

        var audioStream = openSegment.Source
            .GetFrames(cancellationToken)
            .Select(f => f.Data)
            .Prepend(new ActualOpusStreamHeader(audio.CreatedAt, audio.Format).Serialize());
        var publishAudioTask = mustStreamVoice
            ? BackgroundTask.Run(
                () => _audioStreams.Publish(openSegment.StreamId, audioStream),
                Log,
                "Failed to publish audio stream",
                cancellationToken)
            : null;
        var audioEntryTask = mustStreamVoice
            ? BackgroundTask.Run(
                () => CreateAudioEntry(openSegment, beginsAt, recordedAt, cancellationToken),
                Log,
                $"{nameof(CreateAudioEntry)} failed",
                cancellationToken)
            : null;

        var transcribeTask = BackgroundTask.Run(
            () => TranscribeAudio(
                openSegment,
                beginsAt,
                audioEntryTask,
                CancellationToken.None),
            Log,
            $"{nameof(TranscribeAudio)} failed",
            CancellationToken.None);

        // TODO(AY): We should make sure the finalization happens no matter what (later)!
        // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

        if (publishAudioTask != null)
            await publishAudioTask.ConfigureAwait(false);
        var audioEntry = audioEntryTask != null
            ? await audioEntryTask.ConfigureAwait(false)
            : null;

        // Close an open audio segment when the duration becomes available
        await openSegment.Source.WhenDurationAvailable.ConfigureAwait(false);
        openSegment.Close(openSegment.Source.Duration);
        var closedSegment = await openSegment.ClosedSegment.ConfigureAwait(false);
        // We should finalize audio entry regardless of cancellation - that's why CancellationToken.None
        var audioBlobId = mustStreamVoice
            ? await AudioSegmentSaver.Save(closedSegment, CancellationToken.None).ConfigureAwait(false)
            : null;

        if (audioEntry != null)
            await FinalizeAudioEntry(openSegment, audioEntry, audioBlobId, CancellationToken.None)
                .ConfigureAwait(false);

        // And we await for the last "pending" task, which is likely already completed
        var transcribeResult = await transcribeTask.ConfigureAwait(false);
        if (transcribeResult is not null) {
            // Launch re-transcribe after text and audio entries have been finalized.
            await RetranscribeTextEntry(transcribeResult.Value.Item1, transcribeResult.Value.Item2).SilentAwait();
        }
    }

    private async Task<AudioSegmentLanguage> GetTranscriptionLanguage(AudioRecord record, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(record.Session);
        var settings = await kvas.UserChatSettings(record.ChatId).Get(cancellationToken).ConfigureAwait(false);
        var languageSettings = await kvas.UserLanguageSettings().Get(cancellationToken).ConfigureAwait(false);
        return new AudioSegmentLanguage(settings.Language, languageSettings);
    }

    private async Task<TranscriptionEngine> GetTranscriptionEngine(AudioRecord record, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(record.Session);
        var settings = await kvas.UserTranscriptionEngineSettings().Get(cancellationToken).ConfigureAwait(false);
        return settings.TranscriptionEngine;
    }

    private async Task<(TextEntryId, Language)?> TranscribeAudio(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        Task<ChatEntry>? audioEntryTask,
        CancellationToken cancellationToken)
    {
        var (chatLanguage, userLanguageSettings) = audioSegment.Languages;
        if (chatLanguage is not null) {
            var transcriptionOptions = new TranscriptionOptions {
                Language = chatLanguage,
            };
            var textEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, audioEntryTask, cancellationToken).ConfigureAwait(false);
            return textEntryId is not null ? (textEntryId, chatLanguage) : null;
        }
        else {
            var languageCandidates = userLanguageSettings.ListSpoken().ToArray();
            Language? detectedLanguage = null;
            Action<Language[]> onLanguageDetected = languages1 => {
                if (detectedLanguage is not null)
                    return;

                foreach (var languageCandidate in languageCandidates) {
                    if (languages1.Contains(languageCandidate)) {
                        detectedLanguage = languageCandidate;
                        DebugLog?.LogDebug("Detected language: {Language} for AudioSegment: {AudioSegment}", detectedLanguage, audioSegment.StreamId);
                        ApplyTranscriptionDetectedLanguage(audioSegment.Record, detectedLanguage, default);
                        break;
                    }
                }
            };
            var transcriptionOptions = new TranscriptionOptions {
                DetectLanguage = true,
                LanguageCandidates = languageCandidates,
                LanguageDetectedCallback = onLanguageDetected,
            };
            var textEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, audioEntryTask, cancellationToken).ConfigureAwait(false);
            if (detectedLanguage is not null && textEntryId is not null)
                return (textEntryId, detectedLanguage);
            return null;
        }
    }

    private async Task<TextEntryId?> TranscribeAudio(
        OpenAudioSegment audioSegment,
        TranscriptionOptions transcriptionOptions,
        Moment beginsAt,
        Task<ChatEntry>? audioEntryTask,
        CancellationToken cancellationToken)
    {
        TranscriptionEngine transcriptionEngine;
        if (transcriptionOptions.DetectLanguage)
            transcriptionEngine = TranscriptionEngine.Deepgram;
        else
            transcriptionEngine = await GetTranscriptionEngine(audioSegment.Record, cancellationToken).ConfigureAwait(false);
        var transcriber = TranscriberFactory.Get(transcriptionEngine);
        var transcripts = transcriber
            .Transcribe(audioSegment.StreamId.Value, audioSegment.Source, transcriptionOptions, cancellationToken)
            .ThrottleTranscript(Constants.Transcription.ThrottlePeriod, Clocks.CpuClock, cancellationToken)
            .Memoize(CancellationToken.None);
        cancellationToken = CancellationToken.None; // We already accounted for it in TrimOnCancellation

        var transcriptStreamId = audioSegment.StreamId;
        var chatId = audioSegment.Record.ChatId;
        var authorId = audioSegment.Author.Id;
        var repliedEntryId = audioSegment.Record.RepliedEntryId;

        Transcript? lastTranscript = null;
        ChatEntry? textEntry = null;
        ChatEntryLanguage? entryLanguage = null;
        var audioEntry = (ChatEntry?)null;
        try {
            await foreach (var transcript in transcripts.Replay(cancellationToken).ConfigureAwait(false)) {
                lastTranscript = transcript;
                if (entryLanguage?.Languages.Length is null or 0 && textEntry != null)
                    if (lastTranscript.Languages.Length > 0)
                        entryLanguage = await CreateLanguages(lastTranscript.Languages).ConfigureAwait(false);
                if (textEntry != null)
                    continue;
                if (EmptyRegex.IsMatch(transcript.Text))
                    continue;

                // Got first non-empty transcript -> create text entry
                // The code below is performed only once
                textEntry = await CreateTextEntry(transcript).ConfigureAwait(false);
                entryLanguage = await CreateLanguages(lastTranscript.Languages).ConfigureAwait(false);
                var transcriptDiffStream = transcripts.Replay(cancellationToken).ToTranscriptDiffs().Memoize();
                await _transcriptStreams
                    .Publish(transcriptStreamId, transcriptDiffStream)
                    .ConfigureAwait(false);
            }
        }
        finally {
            if (lastTranscript != null && textEntry != null)
                await Task.WhenAll(FinalizeTextEntry(), FinalizeLanguages()).ConfigureAwait(false);
        }
        return textEntry?.Id as TextEntryId;

        async Task<ChatEntry> CreateTextEntry(Transcript transcript)
        {
            audioEntry = audioEntryTask != null
                ? await audioEntryTask.ConfigureAwait(false)
                : null;
            var textEntryId = TextEntryId.New(chatId, 0);
            var repliedEntryLid = repliedEntryId == null
                ? (long?)null
                : repliedEntryId.LocalId;
            var command = new ChatsBackend_ChangeEntry(
                textEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = authorId,
                    Content = "",
                    StreamId = transcriptStreamId.Value,
                    AudioEntryLid = audioEntry?.LocalId,
                    BeginsAt = beginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
                    RepliedEntryLid = repliedEntryLid,
                }));
            textEntry = await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
            DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}",
                textEntry.Id,
                textEntry.ChatId);
            return textEntry;
        }

        async Task FinalizeTextEntry()
        {
            audioEntry ??= audioEntryTask != null
                ? await audioEntryTask.ConfigureAwait(false)
                : null;

            // Final transcript is empty -> remove text entry
            // TODO(AY): Maybe publish [Audio: ...] markup here
            var change = EmptyRegex.IsMatch(lastTranscript.Text)
                ? Change.Remove<ChatEntryDiff>()
                : Change.Update(new ChatEntryDiff {
                    Content = lastTranscript.Text,
                    StreamId = "",
                    AudioEntryLid = audioEntry?.LocalId,
                    EndsAt = beginsAt + TimeSpan.FromSeconds(lastTranscript.TimeRange.End),
                    TimeMap = audioEntry != null
                        ? lastTranscript.TimeMap.Move(-lastTranscript.TextRange.Start, 0)
                        : default,
                });

            var command = new ChatsBackend_ChangeEntry(
                textEntry.Id,
                null, // do not perform version check there - it might have already been changed and it's OK
                change);
            await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
        }

        Task<ChatEntryLanguage> CreateLanguages(Language[] languages)
        {
            var cmd = ChatEntryLanguagesBackend_Change.Upsert(
                new (textEntry.Id) {
                    Languages = languages,
                    EntryContentHash = textEntry.ContentHash,
                });
            return Commander.Call(cmd, true, CancellationToken.None).Require();
        }

        Task FinalizeLanguages()
        {
            if (entryLanguage is null)
                return Task.CompletedTask;

            entryLanguage = entryLanguage with {
                Languages = lastTranscript.Languages,
                EntryContentHash = textEntry.ContentHash,
            };
            var cmd = EmptyRegex.IsMatch(lastTranscript.Text)
                ? ChatEntryLanguagesBackend_Change.Remove(entryLanguage)
                : ChatEntryLanguagesBackend_Change.Upsert(entryLanguage);
            return Commander.Call(cmd, true, CancellationToken.None);
        }
    }

    private async Task<ChatEntry> CreateAudioEntry(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        Moment recordedAt,
        CancellationToken cancellationToken)
    {
        var delay = beginsAt - recordedAt;
        DebugLog?.LogDebug("CreateAudioEntry: delay={Delay:N1}ms", delay.TotalMilliseconds);

        var chatId = audioSegment.Record.ChatId;
        var audioEntryId = AudioEntryId.New(chatId, 0);
        var command = new ChatsBackend_ChangeEntry(
            audioEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = audioSegment.Author.Id,
                Content = "",
                StreamId = audioSegment.StreamId.Value,
                BeginsAt = beginsAt,
                ClientSideBeginsAt = recordedAt,
            }));
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
        var command = new ChatsBackend_ChangeEntry(
            audioEntry.Id,
            null, // do not perform version check there - it might have already been changed and it's OK
            Change.Update(new ChatEntryDiff {
                Content = audioBlobId ?? "",
                StreamId = "",
                EndsAt = endsAt,
                ContentEndsAt = contentEndsAt,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RetranscribeTextEntry(ChatEntryId textEntryId, Language audioSegmentLanguage)
    {
        var command = new ChatsBackend_RetranscribeChatEntry(
            textEntryId,
            audioSegmentLanguage);
        await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
    }

    private void ApplyTranscriptionDetectedLanguage(AudioRecord audioSegmentRecord, Language detectedLanguage,
        CancellationToken cancellationToken)
        => _ = BackgroundTask.Run(async () => {
            var chatId = audioSegmentRecord.ChatId;
            var kvas = ServerKvas.GetClient(audioSegmentRecord.Session);
            var userChatRecordingDetectedLanguage = new UserChatRecordingDetectedLanguage {
                Language = detectedLanguage,
                ChatId = chatId,
                Timestamp = Clocks.SystemClock.Now,
            };
            await kvas.UserChatRecordingDetectedLanguage().Set(userChatRecordingDetectedLanguage, default).ConfigureAwait(false);
        }, Log, "Failed to apply transcription detected language", cancellationToken);
}
