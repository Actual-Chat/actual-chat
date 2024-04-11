using System.Text.RegularExpressions;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

public sealed partial class StreamingBackend
{
    [GeneratedRegex("^\\s*$")]
    private static partial Regex EmptyRegexFactory();
    private static readonly Regex EmptyRegex = EmptyRegexFactory();

    public async Task ProcessAudio(
        AudioRecord record,
        int preSkipFrames,
        RpcStream<AudioFrame> frames,
        CancellationToken cancellationToken)
    {
        ValidateStreamId(record.StreamId);
        Log.LogTrace(nameof(ProcessAudio) + ": record #{StreamId} = {Record}", record.StreamId, record);
        var delayedCts = new CancellationTokenSource();
        var delayedCancellationToken = delayedCts.Token;
 #pragma warning disable MA0147, VSTHRD101
        // ReSharper disable once AsyncVoidLambda
        var registration = cancellationToken.Register(async () => {
            await Task.Delay(Settings.CancellationDelay, CancellationToken.None).ConfigureAwait(false);
            // ReSharper disable once AccessToDisposedClosure
            delayedCts.CancelAndDisposeSilently();
        });
 #pragma warning restore MA0147, VSTHRD101
        try {
            var augmentedFrames = frames.AsAsyncEnumerable();
            if (Constants.DebugMode.AudioRecordingStream)
                augmentedFrames = augmentedFrames.WithLog(Log, nameof(ProcessAudio), cancellationToken);
            await ProcessAudio(record, preSkipFrames, augmentedFrames, delayedCancellationToken).ConfigureAwait(false);
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
            await registration.DisposeAsync().ConfigureAwait(false);
            delayedCts.CancelAndDisposeSilently();
        }
    }

    // Private methods

    public async Task ProcessAudio(
        AudioRecord record,
        int preSkipFrames,
        IAsyncEnumerable<AudioFrame> frames,
        CancellationToken cancellationToken)
    {
        var beginsAt = Clocks.SystemClock.Now;
        var rules = await Chats.GetRules(record.Session, record.ChatId, cancellationToken).ConfigureAwait(false);
        rules.Require(ChatPermissions.Write);

        var language = await GetTranscriptionLanguage(record, cancellationToken).ConfigureAwait(false);
        var languages = ApiArray.New(language);

        var author = await Authors
            .EnsureJoined(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);

        var chatVoiceSettings = new ChatVoiceSettings(Services, new AccountSettings(ServerKvas, record.Session));
        var chatVoiceMode = await chatVoiceSettings
            .Get(record.Session, record.ChatId, cancellationToken)
            .ConfigureAwait(false);
        var mustStreamVoice = chatVoiceMode.VoiceMode.HasVoice();

        var recordedAt = default(Moment) + TimeSpan.FromSeconds(record.ClientStartOffset);
        var audio = new AudioSource(
            new Moment(recordedAt),
            AudioSource.DefaultFormat with { PreSkipFrames = preSkipFrames },
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

        // TODO(AY): We should make sure finalization happens no matter what (later)!
        // TODO(AK): Compensate failures during audio entry creation or saving audio blob (later)

        if (publishAudioTask != null)
            await publishAudioTask.ConfigureAwait(false);
        var audioEntry = audioEntryTask != null
            ? await audioEntryTask.ConfigureAwait(false)
            : null;

        // Close open audio segment when the duration become available
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
        await transcribeTask.ConfigureAwait(false);
    }

    private async Task<Language> GetTranscriptionLanguage(AudioRecord record, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(record.Session);
        var settings = await kvas.GetUserChatSettings(record.ChatId, cancellationToken).ConfigureAwait(false);
        var language = await settings.LanguageOrPrimary(kvas, cancellationToken).ConfigureAwait(false);
        return language;
    }

    private async Task<TranscriptionEngine> GetTranscriptionEngine(AudioRecord record, CancellationToken cancellationToken)
    {
        var kvas = ServerKvas.GetClient(record.Session);
        var settings = await kvas.GetUserTranscriptionEngineSettings(cancellationToken).ConfigureAwait(false);
        return settings.TranscriptionEngine;
    }

    private async Task TranscribeAudio(
        OpenAudioSegment audioSegment,
        Moment beginsAt,
        Task<ChatEntry>? audioEntryTask,
        CancellationToken cancellationToken)
    {
        var transcriptionOptions = new TranscriptionOptions {
            Language = audioSegment.Languages[0],
        };
        var transcriptionEngine = await GetTranscriptionEngine(audioSegment.Record, cancellationToken)
            .ConfigureAwait(false);
        var transcriber = TranscriberFactory.Get(transcriptionEngine);
        var transcripts = transcriber
            .Transcribe(audioSegment.StreamId, audioSegment.Source, transcriptionOptions, cancellationToken)
            .Throttle(Settings.TranscriptDebouncePeriod, Clocks.CpuClock, cancellationToken)
            .SuppressCancellation(CancellationToken.None)
            .Memoize(CancellationToken.None);
        cancellationToken = CancellationToken.None; // We already accounted for it in TrimOnCancellation

        var transcriptStreamId = audioSegment.StreamId;
        var publishTask = _transcriptStreams.Publish(
            transcriptStreamId,
            transcripts.Replay(cancellationToken).ToTranscriptDiffs());
        var textEntryTask = CreateAndFinalizeTextEntry(
            audioSegment.Record.ChatId,
            audioSegment.Author.Id,
            beginsAt,
            audioEntryTask,
            transcriptStreamId,
            audioSegment.Record.RepliedChatEntryId,
            transcripts.Replay(cancellationToken));
        await Task.WhenAll(publishTask, textEntryTask).ConfigureAwait(false);
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
        var entryId = new ChatEntryId(chatId, ChatEntryKind.Audio, 0, AssumeValid.Option);
        var command = new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = audioSegment.Author.Id,
                Content = "",
                StreamId = audioSegment.StreamId,
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
                StreamId = Symbol.Empty,
                EndsAt = endsAt,
                ContentEndsAt = contentEndsAt,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateAndFinalizeTextEntry(
        ChatId chatId,
        AuthorId authorId,
        Moment beginsAt,
        Task<ChatEntry>? audioEntryTask,
        string transcriptStreamId,
        ChatEntryId repliedChatEntryId,
        IAsyncEnumerable<Transcript> transcripts)
    {
        Transcript? lastTranscript = null;
        ChatEntry? textEntry = null;
        var audioEntry = (ChatEntry?)null;
        try {
            await foreach (var transcript in transcripts.ConfigureAwait(false)) {
                lastTranscript = transcript;
                if (textEntry != null)
                    continue;
                if (EmptyRegex.IsMatch(transcript.Text))
                    continue;

                // Got first non-empty transcript -> create text entry
                audioEntry = audioEntryTask != null
                    ? await audioEntryTask.ConfigureAwait(false)
                    : null;
                var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0, AssumeValid.Option);
                var command = new ChatsBackend_ChangeEntry(
                    entryId,
                    null,
                    Change.Create(new ChatEntryDiff {
                        AuthorId = authorId,
                        Content = "",
                        StreamId = transcriptStreamId,
                        AudioEntryId = audioEntry?.LocalId,
                        BeginsAt = beginsAt + TimeSpan.FromSeconds(transcript.TimeRange.Start),
                        RepliedEntryLocalId = repliedChatEntryId is { IsNone: false, LocalId: var localId }
                            ? localId
                            : null,
                    }));
                textEntry = await Commander.Call(command, true, CancellationToken.None).ConfigureAwait(false);
                DebugLog?.LogDebug("CreateTextEntry: #{EntryId} is created in chat #{ChatId}",
                    textEntry.Id,
                    textEntry.ChatId);
            }
        }
        finally {
            if (lastTranscript != null && textEntry != null) {
                audioEntry ??= audioEntryTask != null
                    ? await audioEntryTask.ConfigureAwait(false)
                    : null;

                // Final transcript is empty -> remove text entry
                // TODO(AY): Maybe publish [Audio: ...] markup here
                var change = EmptyRegex.IsMatch(lastTranscript.Text)
                    ? Change.Remove<ChatEntryDiff>()
                    : Change.Update(new ChatEntryDiff {
                        Content = lastTranscript.Text,
                        StreamId = Symbol.Empty,
                        AudioEntryId = audioEntry?.LocalId,
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
        }
    }
}
