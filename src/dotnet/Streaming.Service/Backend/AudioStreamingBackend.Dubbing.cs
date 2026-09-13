using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

public partial class AudioStreamingBackend
{
    private readonly ConcurrentDictionary<StreamId, DubEntry> _dubs = new();
    private readonly ConcurrentDictionary<string, Task> _dubChains = new();

    private ISpeechSynthesizer? SpeechSynthesizer => field ??= Services.GetService<ISpeechSynthesizer>();

    // Private methods

    // Starts the dub of dubStreamId's base stream into dubStreamId.Language unless it's running or
    // decided already. True means the dub stream is published; false means serve the original.
    private async Task<bool> EnsureDub(StreamId dubStreamId, CancellationToken cancellationToken)
    {
        if (SpeechSynthesizer == null)
            return false;

        var entry = _dubs.GetOrAdd(dubStreamId, static (id, self) => self.StartDub(id), this);
        // Start is idempotent, and GetOrAdd may run the factory twice: a loser must never run.
        entry.Worker.Start();
        try {
            return await entry.WhenDecided
                .WaitAsync(Constants.Audio.DubWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            Log.LogWarning("EnsureDub: #{StreamId} - no decision in {Timeout}s, serving the original",
                dubStreamId, Constants.Audio.DubWaitTimeout.TotalSeconds);
            return false;
        }
    }

    private DubEntry StartDub(StreamId dubStreamId)
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
        var sourceStreamId = BaseStreamId(dubStreamId);
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

            var stabilizer = new DubStabilizer();
            var decision = DubDecision.Undecided;
            var translated = Transcript.Empty;
            await foreach (var diff in translatedMemoizer.Replay(cancellationToken).ConfigureAwait(false)) {
                translated += diff;
                if (decision == DubDecision.Undecided) {
                    decision = DubStabilizer.Decide(Fold(sourceMemoizer), translated, language);
                    if (decision == DubDecision.NoDub) {
                        Log.LogInformation("RunDub: #{StreamId} - already in {Language}", dubStreamId, language);
                        return;
                    }
                    if (decision == DubDecision.Dub)
                        synthesizeTask = StartSynthesis(dubStreamId, text.Reader, decidedSource, cancellationToken);
                }
                if (decision != DubDecision.Dub)
                    continue;

                if (stabilizer.Next(translated) is { } chunk)
                    await text.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            if (decision == DubDecision.Undecided)
                Log.LogInformation("RunDub: #{StreamId} - too short to decide, not dubbed", dubStreamId);
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
                var options = new SpeechSynthesisOptions(language);
                await SpeechSynthesizer!
                    .Synthesize(dubStreamId.Value, text, options, frames.Writer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                frames.Writer.TryComplete(e);
                throw;
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
        chainKey = null;
        if (!_authorIdByStream.TryGetValue(BaseStreamId(dubStreamId), out var authorId))
            return Task.CompletedTask;

        chainKey = $"{authorId}~{dubStreamId.Language}";
        var previousDubTask = _dubChains.GetValueOrDefault(chainKey) ?? Task.CompletedTask;
        _dubChains[chainKey] = whenDoneSource.Task;
        return previousDubTask;
    }

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

    private void ForgetDub(StreamId dubStreamId, Task<bool> whenDecided)
    {
        // Only this worker's own entry: a fresh one may already have taken the key
        if (_dubs.TryGetValue(dubStreamId, out var entry) && entry.WhenDecided == whenDecided)
            _dubs.TryRemove(new KeyValuePair<StreamId, DubEntry>(dubStreamId, entry));
    }

    private void ForgetDubs(StreamId streamId)
    {
        var baseStreamId = BaseStreamId(streamId);
        foreach (var dubStreamId in _dubs.Keys)
            if (BaseStreamId(dubStreamId) == baseStreamId && _dubs.TryRemove(dubStreamId, out var entry))
                _ = entry.Worker.DisposeSilentlyAsync();
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
