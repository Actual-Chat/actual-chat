namespace ActualChat.Chat.ML;

public sealed record LiveTagOptions(
    int MinWords,
    int MaxWords,
    int ContextWords,
    int MaxChunks,
    bool IsWordSplittable)
{
    // The first FastChunks chunks go out as soon as they have FirstMinWords, so the first marks show early
    public int FirstMinWords { get; init; } = MinWords;
    public int FastChunks { get; init; }
}

public enum LiveTagOutcome
{
    Complete = 0,
    ChunkLimit,
    TaggerFailed,
    StreamBroken,
}

// Spans are in the offsets of Text; IsComplete is false when some of the text could not be tagged,
// and the caller must then tag the settled text itself
public sealed record LiveTagResult(string Text, ApiArray<SpeechSpan> Spans, bool IsComplete)
{
    public LiveTagOutcome Outcome { get; init; }
    public int Calls { get; init; }
    // How many times text that was already tagged got rewritten, and tagging went back to the change
    public int Restarts { get; init; }
}

/// <summary>
/// Tags a transcript while it is still being spoken: whenever enough whole sentences have settled, they go to
/// the tagger, with the sentence before them as context. A sentence has settled once text follows it.
/// </summary>
public static class SpeechLiveTagger
{
    private const int MaxFailures = 3;

    public static async Task<LiveTagResult> Run(
        IAsyncEnumerable<string> texts,
        Func<string, string?, CancellationToken, Task<ApiArray<SpeechSpan>?>> tagChunk,
        Action<string, ApiArray<SpeechSpan>> onProgress,
        LiveTagOptions options,
        CancellationToken cancellationToken)
    {
        var gate = new object();
        var latest = "";
        var isFinal = false;
        var isBroken = false;
        using var signal = new SemaphoreSlim(0);
        var reader = Task.Run(async () => {
            try {
                await foreach (var text in texts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    lock (gate)
                        latest = text;
                    signal.Release();
                }
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                lock (gate)
                    isBroken = true;
            }
            finally {
                lock (gate)
                    isFinal = true;
                signal.Release();
            }
        }, CancellationToken.None);

        var spans = new List<SpeechSpan>();
        var taggedUpTo = 0;
        var taggedPrefix = "";
        var calls = 0;
        var restarts = 0;
        var failures = 0;
        var outcome = LiveTagOutcome.Complete;
        var isGivenUp = false;
        string text;
        while (true) {
            bool final;
            lock (gate) {
                text = latest;
                final = isFinal;
            }
            if (taggedUpTo > 0 && !text.StartsWith(taggedPrefix)) {
                // Words already tagged were rewritten: the sentences before the change keep their tags, and
                // tagging starts again from the first changed one
                var common = taggedPrefix.AsSpan().CommonPrefixLength(text);
                taggedUpTo = SpeechChunker.SentenceEnds(text, options.IsWordSplittable).LastOrDefault(e => e <= common);
                taggedPrefix = text[..taggedUpTo];
                spans.RemoveAll(s => s.Start + s.Length > taggedUpTo);
                restarts++;
                onProgress(taggedPrefix, spans.ToApiArray());
            }
            var minWords = calls < options.FastChunks ? options.FirstMinWords : options.MinWords;
            var end = final ? text.Length : StableEnd(text, options.IsWordSplittable);
            var isReady = !isGivenUp
                && end > taggedUpTo
                && SpeechChunker.CountUnits(text, taggedUpTo, end, options.IsWordSplittable)
                    >= (final ? 1 : minWords);
            if (isReady) {
                var chunks = SpeechChunker.Split(
                    text[taggedUpTo..end],
                    options.IsWordSplittable,
                    minWords,
                    options.MaxWords,
                    options.ContextWords,
                    int.MaxValue);
                foreach (var chunk in chunks) {
                    if (calls >= options.MaxChunks) {
                        outcome = LiveTagOutcome.ChunkLimit;
                        isGivenUp = true;
                        break;
                    }

                    var chunkStart = taggedUpTo;
                    var context = chunkStart == 0
                        ? null
                        : SpeechChunker.SentenceBefore(
                            text, chunkStart, options.ContextWords, options.IsWordSplittable);
                    calls++;
                    var tagged = await tagChunk(chunk.Text, context.NullIfEmpty(), cancellationToken)
                        .ConfigureAwait(false);
                    if (tagged is null) {
                        if (final || ++failures >= MaxFailures) {
                            outcome = LiveTagOutcome.TaggerFailed;
                            isGivenUp = true;
                        }
                        break;
                    }

                    failures = 0;
                    spans.AddRange(tagged.Value.Select(s => s with { Start = s.Start + chunkStart }));
                    taggedUpTo = chunkStart + chunk.Length;
                    taggedPrefix = text[..taggedUpTo];
                    onProgress(taggedPrefix, spans.ToApiArray());
                }
                if (!isGivenUp && taggedUpTo == end)
                    continue;
            }

            if (final)
                break;

            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            while (signal.CurrentCount > 0)
                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        await reader.ConfigureAwait(false);
        lock (gate) {
            text = latest;
            if (isBroken)
                outcome = LiveTagOutcome.StreamBroken;
        }
        var isComplete = outcome == LiveTagOutcome.Complete && taggedUpTo == text.Length;
        return new LiveTagResult(text, spans.ToApiArray(), isComplete) {
            Outcome = outcome,
            Calls = calls,
            Restarts = restarts,
        };
    }

    // Private methods

    private static int StableEnd(string text, bool isWordSplittable)
        => SpeechChunker.SentenceEnds(text, isWordSplittable).LastOrDefault(e => e < text.Length);
}
