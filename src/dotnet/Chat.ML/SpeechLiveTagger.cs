namespace ActualChat.Chat.ML;

public sealed record LiveTagOptions(int MinWords, int MaxWords, int ContextWords, int MaxChunks, bool IsWordSplittable);

// Spans are in the offsets of Text; IsComplete is false when some of the text could not be tagged,
// and the caller must then tag the settled text itself
public sealed record LiveTagResult(string Text, ApiArray<SpeechSpan> Spans, bool IsComplete);

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
        var failures = 0;
        var isComplete = true;
        var isGivenUp = false;
        string text;
        while (true) {
            bool final;
            lock (gate) {
                text = latest;
                final = isFinal;
            }
            if (taggedUpTo > 0 && !text.StartsWith(taggedPrefix, StringComparison.Ordinal)) {
                // The words already tagged were rewritten: nothing tagged so far can be trusted
                spans.Clear();
                taggedUpTo = 0;
                taggedPrefix = "";
                isComplete = false;
                isGivenUp = true;
            }
            var end = final ? text.Length : StableEnd(text, options.IsWordSplittable);
            var isReady = !isGivenUp
                && end > taggedUpTo
                && SpeechChunker.CountUnits(text, taggedUpTo, end, options.IsWordSplittable) >= (final ? 1 : options.MinWords);
            if (isReady) {
                var chunks = SpeechChunker.Split(
                    text[taggedUpTo..end],
                    options.IsWordSplittable,
                    options.MinWords,
                    options.MaxWords,
                    options.ContextWords,
                    int.MaxValue);
                foreach (var chunk in chunks) {
                    if (calls >= options.MaxChunks) {
                        isComplete = false;
                        isGivenUp = true;
                        break;
                    }

                    var chunkStart = taggedUpTo;
                    var context = chunkStart == 0
                        ? null
                        : SpeechChunker.SentenceBefore(text, chunkStart, options.ContextWords, options.IsWordSplittable);
                    calls++;
                    var tagged = await tagChunk(chunk.Text, context.NullIfEmpty(), cancellationToken).ConfigureAwait(false);
                    if (tagged is null) {
                        if (final || ++failures >= MaxFailures) {
                            isComplete = false;
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
                isComplete = false;
        }
        return new LiveTagResult(text, spans.ToApiArray(), isComplete && taggedUpTo == text.Length);
    }

    // Private methods

    private static int StableEnd(string text, bool isWordSplittable)
        => SpeechChunker.SentenceEnds(text, isWordSplittable).LastOrDefault(e => e < text.Length);
}
