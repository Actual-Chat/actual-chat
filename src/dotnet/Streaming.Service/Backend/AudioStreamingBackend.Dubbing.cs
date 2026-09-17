using ActualChat.Audio;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    // How many of the store's share waits a registered source's audio may trail its registration by
    private const int MaxOriginalWaitPasses = 5;

    private readonly ConcurrentDictionary<StreamId, DubEntry> _dubs = new();
    private readonly ConcurrentDictionary<string, Task> _dubChains = new();
    private readonly ConcurrentDictionary<string, DubActivity> _dubActivities = new();
    // Written by the synthesis task, read by every RunDub
    private long _synthesizerDownUntilTicks;

    private ISpeechSynthesizer? SpeechSynthesizer => field ??= Services.GetService<ISpeechSynthesizer>();
    private SpeakerVoices SpeakerVoices => field ??= Services.GetRequiredService<SpeakerVoices>();

    // Private methods

    private async Task<bool> EnsureDub(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        // Publishes the S~lang mix of dubStreamId's base stream unless it's running already, and returns
        // once the mix has caught up with what the original had buffered; whether a dub goes into it is
        // decided later by the worker. False only means "no synthesizer here".
        if (SpeechSynthesizer == null)
            return false;

        var candidate = NewDub(dubStreamId);
        var entry = _dubs.GetOrAdd(dubStreamId, candidate);
        if (ReferenceEquals(entry, candidate))
            entry.Worker.Start();
        else
            _ = candidate.Worker.DisposeSilentlyAsync();
        await entry.WhenPublished.WaitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private DubEntry NewDub(StreamId dubStreamId)
    {
        var whenPublishedSource = TaskCompletionSourceExt.New();
#pragma warning disable CA2016 // Pass cancellationToken
        var stopTokenSource = HostLifetime.CreateStopTokenSource();
#pragma warning restore CA2016
        var worker = FuncWorker.New(
            static (arg, ct) => arg.self.RunDub(arg.dubStreamId, arg.whenPublishedSource, ct),
            (self: this, dubStreamId, whenPublishedSource),
            stopTokenSource);
        return new DubEntry(worker, whenPublishedSource.Task);
    }

    private async Task RunDub(
        StreamId dubStreamId,
        TaskCompletionSource whenPublishedSource,
        CancellationToken cancellationToken)
    {
        var sourceStreamId = dubStreamId.BaseStreamId;
        // The mix goes out first: the listener hears the original from its first frame, and
        // everything below only decides whether a dub gets summed onto it
        Language language;
        DubLatencyTrace? latencyTrace;
        AsyncMemoizer<AudioFrame>? original;
        VoiceOverMix mix;
        Task mixTask;
        try {
            language = dubStreamId.Language!;
            latencyTrace = _recordedAtByStream.TryGetValue(sourceStreamId, out var recordedAt)
                ? new DubLatencyTrace(dubStreamId, recordedAt, Clocks.ServerClock)
                : null;
            latencyTrace?.OnRequested();
            original = await WaitForOriginal(sourceStreamId, cancellationToken).ConfigureAwait(false);
            var activity = GetDubChainKey(dubStreamId) is { } chainKey
                ? _dubActivities.GetOrAdd(chainKey, static _ => new DubActivity())
                : new DubActivity();
            mix = new VoiceOverMix(original, activity, Clocks, Log);
            if (latencyTrace != null) {
                mix.Mixed += latencyTrace.OnMixed;
                mix.Ducked += latencyTrace.OnDucked;
            }
            mixTask = PublishMix(dubStreamId, mix, out var memoizer, cancellationToken);
            // A listener joining mid-utterance pins its live edge on the published stream as soon as
            // EnsureDub returns: the frames the original had already buffered must be behind that edge
            // by then, or the burst replaying them would be served as live audio
            var caughtUpFrameCount = await mix.WhenCaughtUp.WaitAsync(cancellationToken).ConfigureAwait(false);
            await WaitForBuffered(memoizer, caughtUpFrameCount + 1, cancellationToken).ConfigureAwait(false);
            whenPublishedSource.TrySetResult();
        }
        catch (Exception e) {
            // The waiter in EnsureDub gets the failure too, or it would never return
            whenPublishedSource.TrySetException(e);
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "RunDub: #{StreamId} failed to publish the mix", dubStreamId);
            return;
        }

        var text = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        using var translationCts = cancellationToken.CreateLinkedTokenSource();
        Task<AsyncMemoizer<TranscriptDiff>?>? translationTask = null;
        Task? synthesizeTask = null;
        Exception? error = null;
        try {
            var sourceMemoizer = await WaitForSourceTranscript(sourceStreamId, original, cancellationToken)
                .ConfigureAwait(false);
            if (sourceMemoizer == null) {
                Log.LogWarning("RunDub: #{StreamId} - no transcript to dub", dubStreamId);
                return;
            }

            latencyTrace?.OnSourceReady(Fold(sourceMemoizer).TimeRange.End);

            // Measured when the dub is requested: a listener who joins mid-utterance finds seconds of
            // audio already transcribed, and must not hear that backlog read out before the live text
            var backlogEnd = Fold(sourceMemoizer).TimeRange.End;
            var isLate = backlogEnd > Constants.Audio.DubBacklogThreshold.TotalSeconds;
            var startedAt = CpuTimestamp.Now;
            var stabilizer = new DubStabilizer();
            var spokenChunkCount = 0;
            bool ApplyDecision(DubDecision decision) {
                var downUntil = new Moment(Volatile.Read(ref _synthesizerDownUntilTicks));
                var isSynthesizerDown = downUntil > Clocks.CpuClock.Now;
                latencyTrace?.OnDecided(decision == DubDecision.Dub && !isSynthesizerDown);
                if (decision == DubDecision.NoDub) {
                    Log.LogInformation("RunDub: #{StreamId} - already in {Language}", dubStreamId, language);
                    return false;
                }

                if (isSynthesizerDown) {
                    Log.LogInformation("RunDub: #{StreamId} - skipped, synthesizer down until {DownUntil}",
                        dubStreamId, downUntil);
                    return false;
                }

                Log.LogInformation(
                    "RunDub: #{StreamId} - dubbing, decided {Elapsed:F1}s after the request "
                    + "at {SourceEnd:F1}s of speech",
                    dubStreamId, startedAt.Elapsed.TotalSeconds, Fold(sourceMemoizer).TimeRange.End);
                synthesizeTask = StartSynthesis(
                    dubStreamId, text.Reader, mix, mixTask, latencyTrace, cancellationToken);
                return true;
            }

            // The translation is needed only for a dub, but it's shared with the caption readers
            // anyway, and the source usually decides while it's still being started
            translationTask = WaitForTranslation(dubStreamId, sourceMemoizer, translationCts.Token);
            var decision = await DecideOnSource(sourceMemoizer, language, cancellationToken).ConfigureAwait(false);
            if (decision != DubDecision.Undecided && !ApplyDecision(decision))
                return;

            var translatedMemoizer = await translationTask.ConfigureAwait(false);
            if (translatedMemoizer == null) {
                // A dub already has the synthesis open on the text channel: completing it with nothing
                // written ends the synthesis cleanly, and the mix carries the original either way
                if (decision == DubDecision.Dub)
                    Log.LogWarning("RunDub: #{StreamId} - had nothing to speak: no translation", dubStreamId);
                else
                    Log.LogWarning("RunDub: #{StreamId} - no translation to dub", dubStreamId);
                return;
            }

            var translated = Transcript.Empty;
            bool IsTranslationComplete()
                // The translator scales each increment's time map from the source's, so a translated
                // transcript that reaches the source's end has nothing left to translate
                => translated.IsStable
                    && translated.TimeRange.End + Transcript.TimeMapEpsilon.Y >= Fold(sourceMemoizer).TimeRange.End;
            async Task Speak(string chunk) {
                spokenChunkCount++;
                Log.LogInformation(
                    "RunDub: #{StreamId} - speaking chunk #{Index} ({Length} chars) at {SourceEnd:F1}s of speech",
                    dubStreamId, spokenChunkCount, chunk.Length, Fold(sourceMemoizer).TimeRange.End);
                latencyTrace?.OnSpoken(translated);
                await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            var diffs = ReadTranslation(translatedMemoizer, sourceMemoizer, IsTranslationComplete, cancellationToken);
            await foreach (var diff in diffs.ConfigureAwait(false)) {
                translated += diff;
                latencyTrace?.OnTranslated(translated);
                if (decision == DubDecision.Undecided) {
                    // The source named no language, so the translated text has to tell
                    decision = DubStabilizer.Decide(Fold(sourceMemoizer), translated, language);
                    if (decision == DubDecision.Undecided)
                        continue;
                    if (!ApplyDecision(decision))
                        return;
                }

                // The backlog is skipped whatever decided the dub, and on every transcript: the
                // translation arrives one clause at a time, but a dub attaching after it has moved on
                // gets everything so far folded into one
                if (isLate)
                    stabilizer.Skip(translated, backlogEnd);
                if (stabilizer.Next(translated) is { } chunk)
                    await Speak(chunk).ConfigureAwait(false);
            }
            if (decision == DubDecision.Undecided)
                Log.LogInformation("RunDub: #{StreamId} - too short to decide, not dubbed", dubStreamId);
            else if (decision == DubDecision.Dub && spokenChunkCount == 0 && !isLate)
                Log.LogWarning(
                    "RunDub: #{StreamId} - had nothing to speak: the translation never became stable",
                    dubStreamId);
        }
        catch (Exception e) {
            error = e;
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "RunDub: #{StreamId} failed", dubStreamId);
        }
        finally {
            text.Writer.TryComplete(error);
            // Cancelling this local wait doesn't stop the shared translation, which keeps serving
            // the caption readers from its own worker
            await translationCts.CancelAsync().ConfigureAwait(false);
            if (translationTask != null)
                await translationTask.SilentAwait(false);
            if (synthesizeTask != null)
                await synthesizeTask.SilentAwait(false);
            // The mix ends only once DubPcm is complete: the synthesis completes it on its way out,
            // and every path that never started one completes it here
            mix.DubPcm.TryComplete();
            await mixTask.SilentAwait(false);
            latencyTrace?.Report(Log);
        }
    }

    private async Task<AsyncMemoizer<AudioFrame>?> WaitForOriginal(
        StreamId sourceStreamId,
        CancellationToken cancellationToken)
    {
        // ProcessAudio registers a recording (and remembers its recordedAt) before its DB work and
        // the audio publish that follows, so a registered source is re-asked for a bounded while;
        // a source that was never registered gets the store's single share wait.
        var startedAt = CpuTimestamp.Now;
        var maxWait = _audioStreams.ShareWaitDelay * MaxOriginalWaitPasses;
        var isRegistered = false;
        while (true) {
            var original = await _audioStreams
                .GetMemoizer(sourceStreamId, true, cancellationToken)
                .ConfigureAwait(false);
            if (original != null)
                return original;

            isRegistered = _recordedAtByStream.ContainsKey(sourceStreamId);
            if (!isRegistered || startedAt.Elapsed >= maxWait)
                break;
        }

        if (isRegistered)
            Log.LogWarning(
                "RunDub: #{StreamId} - no audio for a registered source after {Elapsed:F1}s, mixing dub-only",
                sourceStreamId, startedAt.Elapsed.TotalSeconds);
        return null;
    }

    private static async Task<DubDecision> DecideOnSource(
        AsyncMemoizer<TranscriptDiff> sourceMemoizer,
        Language language,
        CancellationToken cancellationToken)
    {
        // Undecided as soon as the source has enough text but no language - a transcriber that tags
        // none would otherwise hold the dub until the source ends; the translated text decides then
        var source = Transcript.Empty;
        var diffs = sourceMemoizer.Replay(cancellationToken);
        await foreach (var diff in diffs.ConfigureAwait(false)) {
            source = TranscriptFolder(source, diff);
            if (source.Text.Length >= DubStabilizer.MinDecisionLength)
                return DubStabilizer.Decide(source, Transcript.Empty, language);
        }

        return DubDecision.Undecided;
    }

    private Task PublishMix(
        StreamId dubStreamId,
        VoiceOverMix mix,
        out AsyncMemoizer<AudioFrame> memoizer,
        CancellationToken cancellationToken)
    {
        // Header-first: the muxer's GetStream(S~lang) succeeds on the publish, before any frame exists
        var frames = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        var header = new AudioFrame {
            Data = new ActualOpusStreamHeader(Clocks.ServerClock.Now, AudioSource.DefaultFormat).Serialize(),
            Offset = TimeSpan.FromMilliseconds(-1),
        };
        memoizer = frames.Reader.ReadAllAsync(cancellationToken).Prepend(header).Memoize(cancellationToken);
        if (!_audioStreams.Publish(dubStreamId, memoizer)) {
            _ = memoizer.DisposeAsync();
            throw StandardError.Internal($"Dub stream #{dubStreamId} is already published.");
        }

        return BackgroundTask.Run(
            () => mix.Run(frames.Writer, cancellationToken),
            Log, $"Dub #{dubStreamId} mix failed");
    }

    private static async Task WaitForBuffered(
        AsyncMemoizer<AudioFrame> memoizer,
        int producedCount,
        CancellationToken cancellationToken)
    {
        // The mix writes its frames to a channel the published memoizer reads a moment later, so what
        // the mix has emitted isn't behind the memoizer's tail - the live edge a joiner pins - just yet
        while (memoizer.ProducedCount < producedCount && !memoizer.IsCompleted)
            await memoizer.WhenChanged(memoizer.ProducedCount).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task StartSynthesis(
        StreamId dubStreamId,
        ChannelReader<string> text,
        VoiceOverMix mix,
        Task mixTask,
        DubLatencyTrace? latencyTrace,
        CancellationToken cancellationToken)
    {
        var language = dubStreamId.Language!;
        var previousDubTask = ChainDub(dubStreamId, out var whenDoneSource, out var chainKey);
        return BackgroundTask.Run(async () => {
            try {
                // One voice must not overlap itself: the author's previous dub in this language
                // may still be draining in its mix after its source ended.
                await previousDubTask.SilentAwait(false);
                var voiceId = await GetSpeakerVoice(dubStreamId, cancellationToken).ConfigureAwait(false);
                latencyTrace?.OnVoice(voiceId);
                var options = new SpeechSynthesisOptions(language, voiceId) { Listener = latencyTrace };
                await SpeechSynthesizer!
                    .Synthesize(dubStreamId.Value, text, options, mix.DubPcm, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                mix.DubPcm.TryComplete(e);
                if (e.IsCancellationOf(cancellationToken))
                    throw;
                if (text.Completion.IsFaulted) {
                    // The failure came in through the text channel (the worker's own failure, already
                    // logged there): the mix carries on with the original alone, and the provider is fine
                    Log.LogInformation("Dub #{StreamId} ended without speech: {Error}", dubStreamId, e.Message);
                    return;
                }

                // The mix carries on with the original alone; later utterances skip the synthesis
                // instead of paying the failure again while the provider is down.
                var downUntil = Clocks.CpuClock.Now + Constants.Audio.DubSynthesizerDownDelay;
                Volatile.Write(ref _synthesizerDownUntilTicks, downUntil.EpochOffsetTicks);
                Log.LogWarning(e, "Dub #{StreamId} failed, no dubs until {DownUntil}", dubStreamId, downUntil);
            }
            finally {
                // The synthesizer is well ahead of the listener: the chain is released once the mix
                // has played the dub out, not once the PCM is written
                mix.DubPcm.TryComplete();
                await mixTask.SilentAwait(false);
                whenDoneSource.TrySetResult();
                if (chainKey != null)
                    _dubChains.TryRemove(new KeyValuePair<string, Task>(chainKey, whenDoneSource.Task));
            }
        }, Log, $"Dub #{dubStreamId} failed");
    }

    private Task ChainDub(StreamId dubStreamId, out TaskCompletionSource whenDoneSource, out string? chainKey)
    {
        whenDoneSource = TaskCompletionSourceExt.New();
        chainKey = GetDubChainKey(dubStreamId);
        if (chainKey == null)
            return Task.CompletedTask;

        var whenDone = whenDoneSource.Task;
        var previousDubTask = Task.CompletedTask;
        _dubChains.AddOrUpdate(chainKey, whenDone, (_, previous) => {
            previousDubTask = previous;
            return whenDone;
        });
        return previousDubTask;
    }

    private Task<string?> GetSpeakerVoice(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        // Read once per dub: a voice change applies from the speaker's next utterance
        var baseStreamId = dubStreamId.BaseStreamId;
        if (!_authorIdByStream.TryGetValue(baseStreamId, out var authorId)
            || !_chatIdByStream.TryGetValue(baseStreamId, out var chatId))
            return Task.FromResult<string?>(null);

        return SpeakerVoices.Get(chatId, authorId, cancellationToken);
    }

    private string? GetDubChainKey(StreamId dubStreamId)
        => _authorIdByStream.TryGetValue(dubStreamId.BaseStreamId, out var authorId)
            ? $"{authorId}~{dubStreamId.Language}"
            : null;

    private async Task<AsyncMemoizer<TranscriptDiff>?> WaitForSourceTranscript(
        StreamId sourceStreamId,
        AsyncMemoizer<AudioFrame>? audio,
        CancellationToken cancellationToken)
    {
        // The source transcript is published on the first non-empty STT result, which can trail
        // the audio by more than ShareWaitDelay - so wait as long as the audio is running, plus
        // one more ShareWaitDelay pass after it ends, since STT itself can trail behind that too.
        // No audio at all (the mix's share wait already missed it) counts as ended.
        var isAudioEnded = false;
        while (true) {
            var memoizer = await _transcriptStreams
                .GetMemoizer(sourceStreamId, true, cancellationToken)
                .ConfigureAwait(false);
            if (memoizer != null || isAudioEnded)
                return memoizer;

            isAudioEnded = audio?.WhenRunning is not { IsCompleted: false };
        }
    }

    private async Task<AsyncMemoizer<TranscriptDiff>?> WaitForTranslation(
        StreamId dubStreamId,
        AsyncMemoizer<TranscriptDiff> sourceMemoizer,
        CancellationToken cancellationToken)
    {
        // ProcessAudio creates the text entry the translation is keyed by ~100 ms after it publishes
        // the transcript, so the first request usually lands in that window: a miss is retried for
        // as long as the source transcript is live.
        while (true) {
            var memoizer = await GetOrStartTranslation(dubStreamId, cancellationToken).ConfigureAwait(false);
            if (memoizer != null || sourceMemoizer.IsCompleted)
                return memoizer;

            await Clocks.CpuClock
                .Delay(Constants.Audio.DubTranslationRetryDelay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // internal for tests
    internal static async IAsyncEnumerable<TranscriptDiff> ReadTranslation(
        AsyncMemoizer<TranscriptDiff> translatedMemoizer,
        AsyncMemoizer<TranscriptDiff> sourceMemoizer,
        Func<bool> isTranslationComplete,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The translated stream stays open until the entry is finalized, which waits for the
        // re-transcription; but nothing more comes once the source has ended and the whole of it is
        // translated (a stable diff alone isn't that: with progressive finals one can land while the
        // last increment is still with the translator), and the author's next dub is chained behind
        // this one - so the read ends at whichever of the two happens second.
        using var replayCts = cancellationToken.CreateLinkedTokenSource();
        var sourceEndTask = sourceMemoizer.WhenRunning ?? Task.CompletedTask;
        var diffs = translatedMemoizer.Replay(replayCts.Token).GetAsyncEnumerator(replayCts.Token);
        try {
            while (true) {
                var moveNextTask = diffs.MoveNextAsync().AsTask();
                if (isTranslationComplete()) {
                    await Task.WhenAny(moveNextTask, sourceEndTask).ConfigureAwait(false);
                    // Re-asked once the source has ended: it may have grown since the last translation
                    if (!moveNextTask.IsCompleted && isTranslationComplete()) {
                        replayCts.Cancel();
                        await moveNextTask.SilentAwait(false);
                        yield break;
                    }
                }
                if (!await moveNextTask.ConfigureAwait(false))
                    yield break;

                yield return diffs.Current;
            }
        }
        finally {
            await diffs.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void ForgetDubs(StreamId streamId)
    {
        // Only the entries: the transcript expires 60 s after it completes, while its dub can still
        // be draining, so the worker ends on its own (WorkerBase disposes its CTS then)
        var baseStreamId = streamId.BaseStreamId;
        foreach (var dubStreamId in _dubs.Keys) {
            if (dubStreamId.BaseStreamId != baseStreamId)
                continue;

            _dubs.TryRemove(dubStreamId, out _);
            // The activity is the author's, not the stream's: it goes with their last dub in the language
            if (GetDubChainKey(dubStreamId) is { } chainKey
                && !_dubChains.ContainsKey(chainKey)
                && !_dubs.Keys.Any(x => GetDubChainKey(x) == chainKey))
                _dubActivities.TryRemove(chainKey, out _);
        }
    }

    private static Transcript Fold(AsyncMemoizer<TranscriptDiff> memoizer)
    {
        var (transcript, _) = memoizer is FoldingAsyncMemoizer<TranscriptDiff, Transcript> folding
            ? folding.Fold()
            : memoizer.FoldBuffered(Transcript.Empty, TranscriptFolder);
        return transcript;
    }

    // Nested types

    private sealed record DubEntry(FuncWorker Worker, Task WhenPublished);
}
