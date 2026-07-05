using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Live;
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
        DebugLog?.LogDebug(nameof(ProcessAudio) + ": record #{StreamId} = {Record}", record.StreamId, record);
        var delayedCts = cancellationToken.CreateDelayedTokenSource(Constants.Transcription.CancellationDelay);
        var delayedCancellationToken = delayedCts.Token;
        try {
            ValidateStreamId(record.StreamId);
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
            // Release the producer's sender: once ProcessAudio returns, nobody
            // will pull from `frames` again, so the far end must stop buffering.
            frames.Disconnect();
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
        using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requestToken = cancellationToken; // real request/processing token, before the watchdog overlays it
        cancellationToken = watchdogCts.Token;
        // Cadence first so it observes raw inbound timing, before the silence watchdog can short-circuit it.
        frames = WithIngressCadenceLog(record.StreamId.Value, frames, Log, cancellationToken);
        frames = WithFrameSilenceWatchdog(
            record.StreamId.Value,
            Constants.Audio.FrameSilenceTimeout,
            frames,
            watchdogCts,
            requestToken,
            cancellationToken);

        var session = record.Session;
        var chatId = record.ChatId;
        // ClientStartAt is the source's Unix-epoch capture timestamp (seconds).
        var sourceStartOffsetSeconds = record.ClientStartAt;
        // Use source's server-synced clock (same as video) for consistent A/V timing.
        // Avoids adding client→server transit time to the timestamp, which would put
        // audio recordedAtMs on a different clock base than video startedAtMs.
        var sourceBeginsAt = default(Moment) + TimeSpan.FromSeconds(sourceStartOffsetSeconds);
        var beginsAt = sourceBeginsAt;
        var serverNow = Clocks.ServerClock.Now;
        var clockDelta = serverNow - beginsAt;
        if (Math.Abs(clockDelta.TotalSeconds) > Constants.Audio.MaxBeginsAtDrift.TotalSeconds) {
            Log.LogWarning(
                "ProcessAudio: source clock skew {ClockDeltaMs:F0}ms for chat {ChatId}, using server clock",
                clockDelta.TotalMilliseconds, chatId);
            beginsAt = serverNow;
        }
        Log.LogInformation(
            "ProcessAudio: chatId={ChatId}, sourceStartOffset={SourceStartOffset:F3}s, delta={DeltaMs:F0}ms",
            chatId, sourceStartOffsetSeconds, clockDelta.TotalMilliseconds);
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);
        rules.Require(ChatPermissions.WriteAudio);

        var languages = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);

        var author = await Authors
            .EnsureJoined(session, chatId, cancellationToken)
            .ConfigureAwait(false);

        var userSettingsUI = Services.UserSettingsUI(session);
        var chatVoiceMode = await userSettingsUI
            .GetChatVoiceMode(chatId, cancellationToken)
            .ConfigureAwait(false);
        // A live-session VoiceMode override (set by a controller via Manage) merges
        // most-restrictive-wins with the user's per-chat VoiceMode.
        var liveState = await LiveSessionsBackend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        var effectiveVoiceMode = (liveState?.Rules ?? SessionRules.Default).Merge(chatVoiceMode.VoiceMode);
        var mustStreamVoice = effectiveVoiceMode.HasVoice();
        var mustTranscribe = effectiveVoiceMode.HasText();
        // Controller turned both voice and transcript off for the session — nothing to contribute live.
        if (!mustStreamVoice && !mustTranscribe)
            return;

        var recordedAt = default(Moment) + TimeSpan.FromSeconds(sourceStartOffsetSeconds);
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

        // Register the voice fan-out stream only in voice mode (only voice is fanned out to peers).
        if (mustStreamVoice) {
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = chatId,
                AuthorId = author.Id,
                StreamId = openSegment.StreamId.Value,
                BeginsAt = beginsAt,
                SourceBeginsAt = sourceBeginsAt,
                Format = audio.Format,
            };
            await LiveAudioBackend.Register(chatId, streamInfo, cancellationToken).ConfigureAwait(false);
        }

        // Join the live session for any live contribution: voice (any chat) or a streamed transcript
        // in a summarized chat. JustText in a plain chat is just a voice-to-text message, not a session.
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        var isSummarized = chat?.IsSummarized ?? false;
        if (mustStreamVoice || isSummarized)
            await LiveSessionsBackend
                .OnStreamRegistered(chatId, author.Id, null, isSummarized, cancellationToken)
                .ConfigureAwait(false);

        var headerFrame = new AudioFrame {
            Data = new ActualOpusStreamHeader(audio.CreatedAt, audio.Format).Serialize(),
            Offset = TimeSpan.FromMilliseconds(-1), // Header marker
        };
        var audioStream = openSegment.Source
            .GetFrames(cancellationToken)
            .Prepend(headerFrame);
        Task? publishAudioTask = null;
        if (mustStreamVoice) {
            var audioMemoizer = audioStream.Memoize(cancellationToken);
            if (_audioStreams.Publish(openSegment.StreamId, audioMemoizer))
                publishAudioTask = BackgroundTask.Run(
                    () => audioMemoizer.WhenRunning ?? Task.CompletedTask,
                    Log,
                    "Failed to publish audio stream",
                    cancellationToken);
            else
                await audioMemoizer.DisposeAsync().ConfigureAwait(false);
        }

        // Pass streamId for Audio.StreamId during live phase; audio MediaId resolved after save
        var liveStreamId = mustStreamVoice ? openSegment.StreamId.Value : null;
        var audioMediaIdTcs = TaskCompletionSourceExt.New<MediaId?>();
        var refineTranscriptLanguageTcs = TaskCompletionSourceExt.New<Language?>();
        var refinedTranscriptTcs = TaskCompletionSourceExt.New<Transcript?>();
        // Transcript off (controller set JustVoice / user JustVoice): live voice still
        // fans out, but no transcription runs and no text entry / audio media is persisted.
        Task? transcribeTask = null;
        if (mustTranscribe)
            transcribeTask = BackgroundTask.Run(
                () => TranscribeAudio(
                    openSegment,
                    beginsAt,
                    liveStreamId,
                    audioMediaIdTcs.Task,
                    refineTranscriptLanguageTcs,
                    refinedTranscriptTcs.Task,
                    CancellationToken.None),
                Log,
                $"{nameof(TranscribeAudio)} failed",
                CancellationToken.None);

        // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

        if (publishAudioTask != null)
            await publishAudioTask.ConfigureAwait(false);

        // Close an open audio segment when the duration becomes available.
        // WhenDurationAvailable ends two ways: "Duration wasn't parsed" when the producer's
        // RPC stream disconnects cleanly mid-recording (non-OCE, caught below), or OCE when
        // the frame-silence watchdog fires because the client lost the network and stopped
        // sending without closing the stream. Both are end-of-audio, not errors: we let
        // FinalizeTextEntry close the entry with whatever transcript we have.
        MediaId? audioMediaId = null;
        ClosedAudioSegment? closedSegment = null;
        try {
            try {
                await openSegment.Source.WhenDurationAvailable.ConfigureAwait(false);
                Log.LogInformation(
                    "ProcessAudio: stream #{StreamId} ended normally, duration={Duration:F1}s",
                    openSegment.StreamId, openSegment.Source.Duration.TotalSeconds);
                openSegment.Close(openSegment.Source.Duration);
                closedSegment = await openSegment.ClosedSegment.ConfigureAwait(false);

                if (mustStreamVoice && mustTranscribe) {
                    // Save audio blob and create Media record - use CancellationToken.None to ensure cleanup
                    audioMediaId = await AudioSegmentSaver
                        .SaveAndCreateMedia(closedSegment, chatId, beginsAt, recordedAt, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogWarning(e,
                    "ProcessAudio: stream #{StreamId} ended unexpectedly; finalizing with available transcript",
                    openSegment.StreamId);
            }
            finally {
                try {
                    if (mustStreamVoice)
                        await LiveAudioBackend
                            .Unregister(chatId, openSegment.StreamId.Value, CancellationToken.None)
                            .ConfigureAwait(false);
                }
                finally {
                    audioMediaIdTcs.TrySetResult(audioMediaId);
                }
            }
        }
        finally {
            // Must run even when WhenDurationAvailable threw OCE (watchdog): DispatchRefineTranscription
            // is the only place refinedTranscriptTcs is completed, and FinalizeTextEntry awaits it —
            // skipping it hangs finalization and strands the entry in the streaming state. Awaiting
            // transcribeTask also keeps `audio` alive until transcription and finalization finish.
            if (mustTranscribe) {
                DispatchRefineTranscription(
                    openSegment, closedSegment, mustStreamVoice, refineTranscriptLanguageTcs.Task, refinedTranscriptTcs);
                await transcribeTask!.ConfigureAwait(false);
            }
        }
    }

    private void DispatchRefineTranscription(
        OpenAudioSegment openSegment,
        ClosedAudioSegment? closedSegment,
        bool mustStreamVoice,
        Task<Language?> refineTranscriptLanguageTask,
        TaskCompletionSource<Transcript?> refinedTranscriptTcs)
    {
        var refineTranscriber = RefineTranscriber;
        if (!mustStreamVoice || refineTranscriber is null || closedSegment is null) {
            refinedTranscriptTcs.TrySetResult(null);
            return;
        }
        var audioSource = closedSegment.Audio;
        var streamId = openSegment.StreamId;
        _ = BackgroundTask.Run(async () => {
            var language = await refineTranscriptLanguageTask.ConfigureAwait(false);
            if (language is not { } lang) {
                refinedTranscriptTcs.TrySetResult(null);
                return;
            }
            using var cts = new CancellationTokenSource(Constants.Transcription.RetranscriptionTimeout);
            try {
                var options = new TranscriptionOptions { Language = lang };
                var result = await refineTranscriber.Transcribe(audioSource, options, cts.Token).ConfigureAwait(false);
                refinedTranscriptTcs.TrySetResult(result);
            }
            catch (Exception ex) {
                Log.LogWarning(ex, "Re-transcription failed for stream #{StreamId}", streamId);
                refinedTranscriptTcs.TrySetResult(null);
            }
        }, Log, "RefineTranscription background task failed", CancellationToken.None);
    }

    private static async IAsyncEnumerable<AudioFrame> WithFrameSilenceWatchdog(
        string streamId,
        TimeSpan silenceTimeout,
        IAsyncEnumerable<AudioFrame> source,
        CancellationTokenSource watchdogCts,
        CancellationToken requestToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = streamId; // reserved for diagnostics
        // Frame-silence watchdog: cancels watchdogCts if no frame arrives within silenceTimeout.
        // Each frame resets the deadline; CancellationTokenSource reuses a single internal timer.
        // On a pure silence timeout (watchdog fired but the request itself wasn't cancelled — i.e. the
        // client dropped the network without closing the stream) we end the stream gracefully instead of
        // surfacing OCE, so the audio that did arrive is saved and the entry finalizes like a short recording.
        watchdogCts.CancelAfter(silenceTimeout);
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try {
            while (true) {
                try {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        yield break;
                }
                catch (OperationCanceledException) when (IsSilenceTimeout(watchdogCts, requestToken)) {
                    yield break;
                }
                watchdogCts.CancelAfter(silenceTimeout);
                yield return enumerator.Current;
            }
        }
        finally {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsSilenceTimeout(CancellationTokenSource watchdogCts, CancellationToken requestToken)
        => watchdogCts.IsCancellationRequested && !requestToken.IsCancellationRequested;

    private static async IAsyncEnumerable<AudioFrame> WithIngressCadenceLog(
        string streamId,
        IAsyncEnumerable<AudioFrame> source,
        ILogger log,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Wall-clock cadence of inbound audio frames per stream. Compared against the
        // client-side `send-cadence` log this isolates whether bursty arrival is the
        // sender's CPU contention vs. transport buffering between client and server.
        // Threshold mirrors the client side (>60ms = at least 3 missed 20ms slots).
        var lastFrameStamp = 0L;
        var lastLogStamp = Stopwatch.GetTimestamp();
        var gapsInWindow = 0;
        var maxGapMs = 0.0;
        await foreach (var frame in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            var nowStamp = Stopwatch.GetTimestamp();
            if (lastFrameStamp != 0) {
                var deltaMs = Stopwatch.GetElapsedTime(lastFrameStamp, nowStamp).TotalMilliseconds;
                if (deltaMs > 60.0) {
                    gapsInWindow++;
                    if (deltaMs > maxGapMs)
                        maxGapMs = deltaMs;
                }
            }
            lastFrameStamp = nowStamp;

            if (Stopwatch.GetElapsedTime(lastLogStamp, nowStamp).TotalSeconds >= 1.0) {
                if (gapsInWindow > 0)
                    log.LogWarning(
                        "audio-ingress-cadence: stream #{StreamId} {Gaps} gap(s) >60ms in last second; max gap {MaxMs:F0}ms",
                        streamId, gapsInWindow, maxGapMs);
                gapsInWindow = 0;
                maxGapMs = 0;
                lastLogStamp = nowStamp;
            }
            yield return frame;
        }
    }

    private async Task<AudioSegmentLanguage> GetTranscriptionLanguage(AudioRecord record, CancellationToken cancellationToken)
    {
        var userSettingsUI = Services.UserSettingsUI(record.Session);
        var settings = await userSettingsUI.ChatUserSettings(record.ChatId).Get(cancellationToken).ConfigureAwait(false);
        var languageSettings = await userSettingsUI.UserLanguageSettings().Get(cancellationToken).ConfigureAwait(false);
        return new AudioSegmentLanguage(settings.Language, languageSettings);
    }

    private async Task<TranscriptionEngine> GetTranscriptionEngine(AudioRecord record, CancellationToken cancellationToken)
    {
        var userSettingsUI = Services.UserSettingsUI(record.Session);
        var settings = await userSettingsUI.UserTranscriptionEngineSettings().Get(cancellationToken).ConfigureAwait(false);
        return settings.TranscriptionEngine;
    }

    private async Task<(ChatEntryId, Language)?> TranscribeAudio(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        string? liveStreamId,
        Task<MediaId?> audioMediaIdTask,
        TaskCompletionSource<Language?> refineTranscriptLanguageTcs,
        Task<Transcript?> refinedTranscriptTask,
        CancellationToken cancellationToken)
    {
        var (chatLanguage, userLanguageSettings) = audioSegment.Languages;
        if (chatLanguage is not null) {
            refineTranscriptLanguageTcs.TrySetResult(chatLanguage);
            var transcriptionOptions = new TranscriptionOptions {
                Language = chatLanguage,
            };
            var chatEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, liveStreamId, audioMediaIdTask, refineTranscriptLanguageTcs, refinedTranscriptTask, cancellationToken).ConfigureAwait(false);
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
            var chatEntryId = await TranscribeAudio(audioSegment, transcriptionOptions, beginsAt, liveStreamId, audioMediaIdTask, refineTranscriptLanguageTcs, refinedTranscriptTask, cancellationToken).ConfigureAwait(false);
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
        TaskCompletionSource<Language?> refineTranscriptLanguageTcs,
        Task<Transcript?> refinedTranscriptTask,
        CancellationToken cancellationToken)
    {
        TranscriptionEngine transcriptionEngine;
        if (transcriptionOptions.DetectLanguage)
            transcriptionEngine = TranscriptionEngine.Deepgram;
        else
            transcriptionEngine = await GetTranscriptionEngine(audioSegment.Record, cancellationToken).ConfigureAwait(false);
        var transcriber = TranscriberFactory.Get(transcriptionEngine);
        // Providers can fail to complete the transcript stream (e.g. a lost Deepgram finalize ack),
        // which would strand the entry in the streaming state - so transcription gets a deadline,
        // anchored at the audio source end. The paced (2x) audio push lags that end by at most
        // a couple of seconds: arrival burstiness is capped by the frame-silence watchdog.
        using var deadlineCts = cancellationToken.CreateLinkedTokenSource();
        _ = audioSegment.Source.WhenDurationAvailable.ContinueWith(
            _ => {
                try {
                    deadlineCts.CancelAfter(Constants.Transcription.CompletionTimeout);
                }
                catch (ObjectDisposedException) { }
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        using var transcripts = transcriber
            .Transcribe(audioSegment.StreamId.Value, audioSegment.Source, transcriptionOptions, deadlineCts.Token)
            .ThrottleTranscript(Constants.Transcription.ThrottlePeriod, Clocks.CpuClock, cancellationToken)
            .Memoize(CancellationToken.None);
        cancellationToken = CancellationToken.None; // Past this point only deadlineCts cancels the transcriber

        var transcriptStreamId = audioSegment.StreamId;
        var chatId = audioSegment.Record.ChatId;
        var authorId = audioSegment.Author.Id;
        var repliedEntryId = audioSegment.Record.RepliedEntryId;

        AsyncMemoizer<TranscriptDiff>? transcriptDiffStream = null;
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

                // Got first non-empty transcript -> create text entry, so the code below is performed only once

                transcriptDiffStream = transcripts
                    .Replay(cancellationToken)
                    .ToTranscriptDiffs()
                    .Memoize(cancellationToken);
#pragma warning disable CA2025 // transcriptDiffStream must be disposed after publishTranscriptStreamTask completes
                Task publishTranscriptStreamTask;
                if (_transcriptStreams.Publish(transcriptStreamId, transcriptDiffStream))
                    publishTranscriptStreamTask = transcriptDiffStream.WhenRunning ?? Task.CompletedTask;
                else {
                    await transcriptDiffStream.DisposeAsync().ConfigureAwait(false);
                    publishTranscriptStreamTask = Task.CompletedTask;
                }
#pragma warning restore CA2025

                // Wait 0.1s for publishTranscriptStreamTask to publish the stream.
                // We want this to happen BEFORE creating the entry to avoid a race condition
                // where the entry is created before the stream is published.
                // See how TranscriptStreamReader.ProcessTranscriptWithRetry and .RetryDelays,
                // it retries with backoff if gets null.
                await Task.Delay(TimeSpan.FromSeconds(0.1), cancellationToken).ConfigureAwait(false);

                textEntry = await CreateTextEntry(transcript).ConfigureAwait(false);
                // NOTE(DF): in detect language mode, we should persist languages only on text entry finalization.
                if (!transcriptionOptions.DetectLanguage)
                    entryLanguage = await CreateLanguages(lastTranscript.Languages).ConfigureAwait(false);

                await publishTranscriptStreamTask.ConfigureAwait(false);
            }
        }
        catch (Exception e) when (deadlineCts.IsCancellationRequested) {
            Log.LogWarning(e,
                "TranscribeAudio: realtime transcription of #{StreamId} hit the completion deadline, finalizing with what we have",
                audioSegment.StreamId);
        }
        finally {
            if (lastTranscript != null && textEntry != null) {
                // The entry may have been removed by the user or already finalized by
                // ChatEntryFixupFlow while we were running. Both are legitimate races
                // now that streaming entries are user-removable and the fix-up flow
                // self-heals stuck entries. Re-check before issuing the update.
                var current = await ChatsBackend.GetEntry(textEntry.Id, CancellationToken.None).ConfigureAwait(false);
                if (current is null || current.IsRemoved)
                    Log.LogWarning("TranscribeAudio: entry #{EntryId} was removed, skipping finalize", textEntry.Id);
                else if (!current.IsContentStreaming)
                    Log.LogWarning("TranscribeAudio: entry #{EntryId} was already finalized, skipping", textEntry.Id);
                else {
                    // Unblock refine transcription with the language that will be persisted:
                    // - chat-language mode: outer already set it; this is a no-op.
                    // - detect mode: realtime stream has drained, so lastTranscript.Languages is final.
                    refineTranscriptLanguageTcs.TrySetResult(lastTranscript.Languages.FirstOrDefault());
                    await Task.WhenAll(FinalizeTextEntry(), FinalizeLanguages()).ConfigureAwait(false);
                }
            }
            // No-op when finalization set the language above; otherwise refine must not stay blocked on it
            refineTranscriptLanguageTcs.TrySetResult(null);
            await transcriptDiffStream.DisposeSilentlyAsync().ConfigureAwait(false);
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
            var audioMediaId = await audioMediaIdTask.ConfigureAwait(false);
            var refinedTranscript = await refinedTranscriptTask.ConfigureAwait(false);

            var hasAudio = liveStreamId != null;
            var realtimeText = lastTranscript.Text;
            var realtimeTimeMap = lastTranscript.TimeMap.Move(-lastTranscript.TextRange.Start, 0);

            var finalText = realtimeText;
            var finalTimeMap = realtimeTimeMap;
            if (refinedTranscript is not null) {
                if (realtimeText.ShouldUseOriginalTranscript(refinedTranscript.Text))
                    Log.LogInformation(
                        "TranscribeAudio: entry #{EntryId} rejected refined transcript. Original: '{OriginalTranscript}', refined: '{RefinedTranscript}'",
                        textEntry.Id, realtimeText, refinedTranscript.Text);
                else {
                    finalText = refinedTranscript.Text;
                    finalTimeMap = refinedTranscript.TimeMap.IsDegenerate && !realtimeTimeMap.IsDegenerate
                        ? LinearMapDtwRemapper.Remap(realtimeText, refinedTranscript.Text, realtimeTimeMap, LinearMapAlignmentMode.RetranscribeSameAudio)
                        : refinedTranscript.TimeMap;
                }
            }

            var change = EmptyRegex.IsMatch(realtimeText)
                ? Change.Remove<ChatEntryDiff>()
                : Change.Update(new ChatEntryDiff {
                    Content = finalText,
                    ContentStreamId = "",
                    Audio = hasAudio && audioMediaId != null
                        ? new ChatEntryAudio {
                            MediaId = audioMediaId,
                            TimeMap = finalTimeMap,
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

    private void ApplyTranscriptionDetectedLanguage(AudioRecord audioSegmentRecord, Language detectedLanguage,
        CancellationToken cancellationToken)
        => _ = BackgroundTask.Run(async () => {
            var chatId = audioSegmentRecord.ChatId;
            var userSettingsUI = Services.UserSettingsUI(audioSegmentRecord.Session);
            var userChatRecordingDetectedLanguage = new UserChatRecordingDetectedLanguage {
                Language = detectedLanguage,
                ChatId = chatId,
                Timestamp = Clocks.SystemClock.Now,
            };
            await userSettingsUI.UserChatRecordingDetectedLanguage()
                .Set(userChatRecordingDetectedLanguage, cancellationToken)
                .ConfigureAwait(false);
        }, Log, "Failed to apply transcription detected language", cancellationToken);
}
