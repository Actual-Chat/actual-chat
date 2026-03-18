using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Live;
using ActualChat.Media;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
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
            IAsyncEnumerable<AudioFrame> augmentedFrames = frames;
            if (Constants.DebugMode.AudioRecordingStream)
                augmentedFrames = augmentedFrames.WithLog(Log, nameof(ProcessAudio), cancellationToken);
            await ProcessAudio(record, preSkip, augmentedFrames, delayedCancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Error processing audio stream #{StreamId}", record.StreamId);
            throw;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Cancelled processing audio stream #{StreamId}", record.StreamId);
            throw;
        }
        finally {
            delayedCts.CancelAndDisposeSilently();
        }
    }

    // Private methods

    private async Task ProcessAudio(
        AudioRecord record,
        int preSkip,
        IAsyncEnumerable<AudioFrame> frames,
        CancellationToken cancellationToken)
    {
        var session = record.Session;
        var chatId = record.ChatId;
        var beginsAt = Clocks.SystemClock.Now;
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var languages = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);

        var author = await Authors
            .EnsureJoined(session, chatId, cancellationToken)
            .ConfigureAwait(false);

        var accountSettings = new AccountSettings(ServerKvas, session);
        var chatVoiceMode = await accountSettings
            .GetChatVoiceMode(chatId, cancellationToken)
            .ConfigureAwait(false);
        var mustStreamVoice = chatVoiceMode.VoiceMode.HasVoice();

        var recordedAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);
        using var audio = new AudioSource(
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

        // Register active stream as early as possible (before creating ChatEntry)
        if (mustStreamVoice) {
            var activeStream = new LiveStreamInfo {
                ChatId = chatId,
                AuthorId = author.Id,
                StreamId = openSegment.StreamId.Value,
                BeginsAt = beginsAt,
                Format = audio.Format,
            };
            await LiveBackend.RegisterActiveStream(chatId, activeStream, cancellationToken).ConfigureAwait(false);
        }

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

        // Pass streamId for Audio.StreamId during live phase; audio MediaId resolved after save
        var liveStreamId = mustStreamVoice ? openSegment.StreamId.Value : null;
        var audioMediaIdTcs = TaskCompletionSourceExt.New<MediaId?>();
        var transcribeTask = BackgroundTask.Run(
            () => TranscribeAudio(
                openSegment,
                beginsAt,
                liveStreamId,
                audioMediaIdTcs.Task,
                CancellationToken.None),
            Log,
            $"{nameof(TranscribeAudio)} failed",
            CancellationToken.None);

        // TODO(AY): We should make sure the finalization happens no matter what (later)!
        // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

        if (publishAudioTask != null)
            await publishAudioTask.ConfigureAwait(false);

        // Close an open audio segment when the duration becomes available
        await openSegment.Source.WhenDurationAvailable.ConfigureAwait(false);
        openSegment.Close(openSegment.Source.Duration);
        var closedSegment = await openSegment.ClosedSegment.ConfigureAwait(false);

        MediaId? audioMediaId = null;
        if (mustStreamVoice) {
            // Unregister active stream from LiveBackend (use CancellationToken.None to ensure cleanup happens)
            await LiveBackend.UnregisterActiveStream(chatId, openSegment.StreamId.Value, CancellationToken.None).ConfigureAwait(false);
            // Save audio blob and create Media record - use CancellationToken.None to ensure cleanup
            audioMediaId = await AudioSegmentSaver
                .SaveAndCreateMedia(closedSegment, chatId, beginsAt, recordedAt, CancellationToken.None)
                .ConfigureAwait(false);
        }
        audioMediaIdTcs.TrySetResult(audioMediaId);

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

    private async Task<(ChatEntryId, Language)?> TranscribeAudio(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        string? liveStreamId,
        Task<MediaId?> audioMediaIdTask,
        CancellationToken cancellationToken)
    {
        var (chatLanguage, userLanguageSettings) = audioSegment.Languages;
        if (chatLanguage is not null) {
            var transcriptionOptions = new TranscriptionOptions {
                Language = chatLanguage,
            };
            var chatEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, liveStreamId, audioMediaIdTask, cancellationToken).ConfigureAwait(false);
            return chatEntryId is not null ? (chatEntryId, chatLanguage) : null;
        }
        else {
            var languageCandidates = userLanguageSettings.ListSpoken().ToArray();
            Language? detectedLanguage = null;
            Action<Language[]> onLanguageDetected = detectedLanguages => {
                if (detectedLanguage is not null)
                    return;

                foreach (var languageCandidate in languageCandidates) {
                    if (detectedLanguages.Contains(languageCandidate)) {
                        detectedLanguage = languageCandidate;
                        DebugLog?.LogDebug("Detected language: {Language} for AudioSegment: {AudioSegment}", detectedLanguage, audioSegment.StreamId);
                        ApplyTranscriptionDetectedLanguage(audioSegment.Record, detectedLanguage, default);
                        break;
                    }
                }
            };
            var transcriptionOptions = TranscriptionOptions.AutoDetectLanguage(languageCandidates, onLanguageDetected);
            var chatEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, liveStreamId, audioMediaIdTask, cancellationToken).ConfigureAwait(false);
            if (detectedLanguage is not null && chatEntryId is not null)
                return (chatEntryId, detectedLanguage);
            return null;
        }
    }

    private async Task<ChatEntryId?> TranscribeAudio(
        OpenAudioSegment audioSegment,
        TranscriptionOptions transcriptionOptions,
        Moment beginsAt,
        string? liveStreamId,
        Task<MediaId?> audioMediaIdTask,
        CancellationToken cancellationToken)
    {
        TranscriptionEngine transcriptionEngine;
        if (transcriptionOptions.DetectLanguage)
            transcriptionEngine = TranscriptionEngine.Deepgram;
        else
            transcriptionEngine = await GetTranscriptionEngine(audioSegment.Record, cancellationToken).ConfigureAwait(false);
        var transcriber = TranscriberFactory.Get(transcriptionEngine);
        using var transcripts = transcriber
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
        try {
            await foreach (var transcript in transcripts.Replay(cancellationToken).ConfigureAwait(false)) {
                lastTranscript = transcript;
                // NOTE(DF): in detect language mode, we should persist languages only on text entry finalization.
                if (!transcriptionOptions.DetectLanguage)
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
                // NOTE(DF): in detect language mode, we should persist languages only on text entry finalization.
                if (!transcriptionOptions.DetectLanguage)
                    entryLanguage = await CreateLanguages(lastTranscript.Languages).ConfigureAwait(false);
                var transcriptDiffStream = transcripts
                    .Replay(cancellationToken)
                    .ToTranscriptDiffs()
                    .Memoize(cancellationToken);
                await _transcriptStreams
                    .Publish(transcriptStreamId, transcriptDiffStream)
                    .ConfigureAwait(false);
            }
        }
        finally {
            if (lastTranscript != null && textEntry != null)
                await Task.WhenAll(FinalizeTextEntry(), FinalizeLanguages()).ConfigureAwait(false);
        }
        return textEntry?.Id;

        async Task<ChatEntry> CreateTextEntry(Transcript transcript)
        {
            var chatEntryId = ChatEntryId.New(chatId, 0);
            var repliedEntryLid = repliedEntryId == null
                ? (long?)null
                : repliedEntryId.LocalId;
            var command = new ChatsBackend_ChangeEntry(
                chatEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = authorId,
                    Content = "",
                    ContentStreamId = transcriptStreamId.Value,
                    Audio = liveStreamId != null
                        ? new ChatEntryAudio { StreamId = liveStreamId }
                        : null,
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
            // Wait for audio save to complete and get MediaId
            var audioMediaId = await audioMediaIdTask.ConfigureAwait(false);

            // Final transcript is empty -> remove text entry
            // TODO(AY): Maybe publish [Audio: ...] markup here
            var hasAudio = liveStreamId != null;
            var change = EmptyRegex.IsMatch(lastTranscript.Text)
                ? Change.Remove<ChatEntryDiff>()
                : Change.Update(new ChatEntryDiff {
                    Content = lastTranscript.Text,
                    ContentStreamId = "",
                    Audio = hasAudio && audioMediaId != null
                        ? new ChatEntryAudio {
                            MediaId = audioMediaId,
                            TimeMap = lastTranscript.TimeMap.Move(-lastTranscript.TextRange.Start, 0),
                        }
                        : null,
                    EndsAt = beginsAt + TimeSpan.FromSeconds(lastTranscript.TimeRange.End),
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

    private async Task RetranscribeTextEntry(ChatEntryId chatEntryId, Language audioSegmentLanguage)
    {
        var command = new ChatsBackend_RetranscribeChatEntry(
            chatEntryId,
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
            await kvas.UserChatRecordingDetectedLanguage().Set(userChatRecordingDetectedLanguage, cancellationToken).ConfigureAwait(false);
        }, Log, "Failed to apply transcription detected language", cancellationToken);
}
