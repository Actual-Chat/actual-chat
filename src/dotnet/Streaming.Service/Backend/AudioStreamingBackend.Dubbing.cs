using ActualChat.Audio;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    private readonly ConcurrentDictionary<StreamId, DubEntry> _dubs = new();
    private readonly ConcurrentDictionary<string, Task> _dubChains = new();
    private readonly ConcurrentDictionary<string, Moment> _dubCooldowns = new();
    // Written by the synthesis task, read by every EnsureDub
    private long _synthesizerDownUntilTicks;

    private ISpeechSynthesizer? SpeechSynthesizer => field ??= Services.GetService<ISpeechSynthesizer>();
    private SpeakerVoices SpeakerVoices => field ??= Services.GetRequiredService<SpeakerVoices>();

    // Private methods

    private async Task<bool> EnsureDub(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        // Starts the dub of dubStreamId's base stream into dubStreamId.Language unless it's running
        // or decided already. True means the dub stream is published; false means serve the original.
        if (SpeechSynthesizer == null || IsCoolingDown(dubStreamId))
            return false;

        var candidate = NewDub(dubStreamId);
        var entry = _dubs.GetOrAdd(dubStreamId, candidate);
        if (ReferenceEquals(entry, candidate))
            entry.Worker.Start();
        else
            _ = candidate.Worker.DisposeSilentlyAsync();
        try {
            return await entry.WhenDecided
                .WaitAsync(Constants.Audio.DubWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("EnsureDub: #{StreamId} - no decision in {Timeout}s, serving the original",
                dubStreamId, Constants.Audio.DubWaitTimeout.TotalSeconds);
            StartCooldown(dubStreamId);
            return false;
        }
    }

    private DubEntry NewDub(StreamId dubStreamId)
    {
        var decidedSource = TaskCompletionSourceExt.New<bool>();
#pragma warning disable CA2016 // Pass cancellationToken
        var stopTokenSource = HostLifetime.CreateStopTokenSource();
#pragma warning restore CA2016
        var worker = FuncWorker.New(
            static (arg, ct) => arg.self.RunDub(arg.dubStreamId, arg.decidedSource, ct),
            (self: this, dubStreamId, decidedSource),
            stopTokenSource);
        return new DubEntry(worker, decidedSource.Task);
    }

    private async Task RunDub(
        StreamId dubStreamId,
        TaskCompletionSource<bool> decidedSource,
        CancellationToken cancellationToken)
    {
        var sourceStreamId = dubStreamId.BaseStreamId;
        var language = dubStreamId.Language!;
        var text = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        Task? synthesizeTask = null;
        Exception? error = null;
        try {
            var sourceMemoizer = await WaitForSourceTranscript(sourceStreamId, cancellationToken).ConfigureAwait(false);
            if (sourceMemoizer == null) {
                Log.LogWarning("RunDub: #{StreamId} - no transcript to dub", dubStreamId);
                // A miss isn't a decision: drop the entry so the next GetAudio retries instead of inheriting it
                ForgetDub(dubStreamId, decidedSource.Task);
                return;
            }

            var translatedMemoizer = await WaitForTranslation(dubStreamId, sourceMemoizer, cancellationToken)
                .ConfigureAwait(false);
            if (translatedMemoizer == null) {
                Log.LogWarning("RunDub: #{StreamId} - no translation to dub", dubStreamId);
                ForgetDub(dubStreamId, decidedSource.Task);
                return;
            }

            // Measured when the dub is requested: a listener who joins mid-utterance finds seconds of
            // audio already transcribed, and must not hear that backlog read out before the live text
            var isLate = Fold(sourceMemoizer).TimeRange.End > Constants.Audio.DubBacklogThreshold.TotalSeconds;
            var startedAt = CpuTimestamp.Now;
            var stabilizer = new DubStabilizer();
            var decision = DubDecision.Undecided;
            var translated = Transcript.Empty;
            var spokenChunkCount = 0;
            bool IsTranslationComplete()
                // The translator scales each increment's time map from the source's, so a translated
                // transcript that reaches the source's end has nothing left to translate
                => translated.IsStable
                    && translated.TimeRange.End + Transcript.TimeMapEpsilon.Y >= Fold(sourceMemoizer).TimeRange.End;
            var diffs = ReadTranslation(translatedMemoizer, sourceMemoizer, IsTranslationComplete, cancellationToken);
            await foreach (var diff in diffs.ConfigureAwait(false)) {
                translated += diff;
                if (decision == DubDecision.Undecided) {
                    decision = DubStabilizer.Decide(Fold(sourceMemoizer), translated, language);
                    if (decision == DubDecision.NoDub) {
                        Log.LogInformation("RunDub: #{StreamId} - already in {Language}", dubStreamId, language);
                        return;
                    }
                    if (decision == DubDecision.Dub) {
                        Log.LogInformation(
                            "RunDub: #{StreamId} - dubbing, decided {Elapsed:F1}s after the request "
                            + "at {SourceEnd:F1}s of speech",
                            dubStreamId, startedAt.Elapsed.TotalSeconds, Fold(sourceMemoizer).TimeRange.End);
                        if (isLate)
                            stabilizer.Skip(translated);
                        synthesizeTask = StartSynthesis(dubStreamId, text.Reader, decidedSource, cancellationToken);
                    }
                }
                if (decision != DubDecision.Dub)
                    continue;

                if (stabilizer.Next(translated) is { } chunk) {
                    spokenChunkCount++;
                    Log.LogInformation(
                        "RunDub: #{StreamId} - speaking chunk #{Index} ({Length} chars) at {SourceEnd:F1}s of speech",
                        dubStreamId, spokenChunkCount, chunk.Length, Fold(sourceMemoizer).TimeRange.End);
                    await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
            }
            if (decision == DubDecision.Undecided)
                Log.LogInformation("RunDub: #{StreamId} - too short to decide, not dubbed", dubStreamId);
            else if (decision == DubDecision.Dub && spokenChunkCount == 0 && !isLate) {
                // The language decided "dub" but the translation never became stable: a clean end here
                // would leave the listener with a header-only track and no fallback to the original
                error = StandardError.External($"Dub #{dubStreamId} got no stable text to speak.");
                Log.LogWarning("RunDub: #{StreamId} - no stable text to speak, failing the dub", dubStreamId);
            }
        }
        catch (Exception e) {
            error = e;
            if (!e.IsCancellationOf(cancellationToken))
                Log.LogError(e, "RunDub: #{StreamId} failed", dubStreamId);
        }
        finally {
            decidedSource.TrySetResult(false);
            text.Writer.TryComplete(error);
            if (synthesizeTask != null)
                await synthesizeTask.SilentAwait(false);
        }
    }

    private Task StartSynthesis(
        StreamId dubStreamId,
        ChannelReader<string> text,
        TaskCompletionSource<bool> decidedSource,
        CancellationToken cancellationToken)
    {
        var language = dubStreamId.Language!;
        var frames = Channel.CreateUnbounded<AudioFrame>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
        });
        var header = new AudioFrame {
            Data = new ActualOpusStreamHeader(Clocks.ServerClock.Now, AudioSource.DefaultFormat).Serialize(),
            Offset = TimeSpan.FromMilliseconds(-1),
        };
        var memoizer = frames.Reader.ReadAllAsync(cancellationToken).Prepend(header).Memoize(cancellationToken);
        if (!_audioStreams.Publish(dubStreamId, memoizer)) {
            _ = memoizer.DisposeAsync();
            throw StandardError.Internal($"Dub stream #{dubStreamId} is already published.");
        }

        decidedSource.TrySetResult(true);
        var previousDubTask = ChainDub(dubStreamId, out var whenDoneSource, out var chainKey);
        return BackgroundTask.Run(async () => {
            try {
                // One voice must not overlap itself: the author's previous dub in this language
                // may still be draining after its source ended.
                await previousDubTask.SilentAwait(false);
                var voiceId = await GetSpeakerVoice(dubStreamId, cancellationToken).ConfigureAwait(false);
                var options = new SpeechSynthesisOptions(language, voiceId);
                await SpeechSynthesizer!
                    .Synthesize(dubStreamId.Value, text, options, frames.Writer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                frames.Writer.TryComplete(e);
                if (e.IsCancellationOf(cancellationToken))
                    throw;
                if (text.Completion.IsFaulted) {
                    // The failure came in through the text channel (translation, nothing to speak):
                    // the muxer still falls back, but the provider is fine
                    Log.LogInformation("Dub #{StreamId} ended without speech: {Error}", dubStreamId, e.Message);
                    return;
                }

                // The muxer falls back to the original on the erroring stream; later utterances skip
                // the hold and the failure instead of paying both again while the provider is down.
                var downUntil = Clocks.CpuClock.Now + Constants.Audio.DubSynthesizerDownDelay;
                Volatile.Write(ref _synthesizerDownUntilTicks, downUntil.EpochOffsetTicks);
                Log.LogWarning(e, "Dub #{StreamId} failed, no dubs until {DownUntil}", dubStreamId, downUntil);
            }
            finally {
                frames.Writer.TryComplete();
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

    private async Task<string?> GetSpeakerVoice(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        // Read once per dub: a voice change applies from the speaker's next utterance
        var baseStreamId = dubStreamId.BaseStreamId;
        if (!_authorIdByStream.TryGetValue(baseStreamId, out var authorId)
            || !_chatIdByStream.TryGetValue(baseStreamId, out var chatId))
            return null;

        try {
            return await SpeakerVoices.Get(chatId, authorId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Dub #{StreamId}: failed to read the speaker's voice, using the default", dubStreamId);
            return null;
        }
    }

    private bool IsCoolingDown(StreamId dubStreamId)
    {
        var now = Clocks.CpuClock.Now;
        if (new Moment(Volatile.Read(ref _synthesizerDownUntilTicks)) > now)
            return true;

        return GetDubChainKey(dubStreamId) is { } key
            && _dubCooldowns.TryGetValue(key, out var until)
            && until > now;
    }

    private void StartCooldown(StreamId dubStreamId)
    {
        // After a timed-out decision the next utterances of this author are served undubbed at
        // once rather than each paying the hold; logged once per cool-down.
        if (GetDubChainKey(dubStreamId) is not { } key)
            return;

        var now = Clocks.CpuClock.Now;
        if (_dubCooldowns.TryGetValue(key, out var current) && current > now)
            return;

        foreach (var (expiredKey, expiredUntil) in _dubCooldowns)
            if (expiredUntil <= now)
                _dubCooldowns.TryRemove(new KeyValuePair<string, Moment>(expiredKey, expiredUntil));
        var until = now + Constants.Audio.DubCooldown;
        _dubCooldowns[key] = until;
        Log.LogWarning("StartCooldown: {Key} - serving the original without a hold until {Until}", key, until);
    }

    private string? GetDubChainKey(StreamId dubStreamId)
        => _authorIdByStream.TryGetValue(dubStreamId.BaseStreamId, out var authorId)
            ? $"{authorId}~{dubStreamId.Language}"
            : null;

    private async Task<AsyncMemoizer<TranscriptDiff>?> WaitForSourceTranscript(
        StreamId sourceStreamId,
        CancellationToken cancellationToken)
    {
        // The source transcript is published on the first STT result, which can trail the audio by
        // more than ShareWaitDelay, so keep waiting for as long as the audio itself is live.
        while (true) {
            var memoizer = await _transcriptStreams
                .GetMemoizer(sourceStreamId, true, cancellationToken)
                .ConfigureAwait(false);
            if (memoizer != null || !_audioStreams.Has(sourceStreamId))
                return memoizer;
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

    private void ForgetDub(StreamId dubStreamId, Task<bool> whenDecided)
    {
        // Only this worker's own entry: a fresh one may already have taken the key
        if (_dubs.TryGetValue(dubStreamId, out var entry) && entry.WhenDecided == whenDecided)
            _dubs.TryRemove(new KeyValuePair<StreamId, DubEntry>(dubStreamId, entry));
    }

    private void ForgetDubs(StreamId streamId)
    {
        // Only the entries: the transcript expires 60 s after it completes, while its dub can still
        // be draining, so the worker ends on its own (WorkerBase disposes its CTS then)
        var baseStreamId = streamId.BaseStreamId;
        foreach (var dubStreamId in _dubs.Keys)
            if (dubStreamId.BaseStreamId == baseStreamId)
                _dubs.TryRemove(dubStreamId, out _);
    }

    private static Transcript Fold(AsyncMemoizer<TranscriptDiff> memoizer)
    {
        var (transcript, _) = memoizer is FoldingAsyncMemoizer<TranscriptDiff, Transcript> folding
            ? folding.Fold()
            : memoizer.FoldBuffered(Transcript.Empty, TranscriptFolder);
        return transcript;
    }

    // Nested types

    private sealed record DubEntry(FuncWorker Worker, Task<bool> WhenDecided);
}
