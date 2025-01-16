using ActualChat.Chat;
using ActualChat.Chat.ML;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal sealed class ChatContentArranger3(
    IDocumentEntriesLoader documentEntriesLoader,
    IEmbeddingsCalculator embeddingsCalculator,
    IChatDialogFormatter chatDialogFormatter
) : IChatContentArranger
{
    public const int WordCountPerBlock = 100;
    public const int MinBlockLength = WordCountPerBlock * 2 / 5;

    public Action<string>? DebugLog { get; set; }

    public async IAsyncEnumerable<SourceEntries> ArrangeAsync(
        IReadOnlyCollection<ChatEntry> bufferedEntries,
        IReadOnlyCollection<ChatSlice> tailDocuments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (bufferedEntries.Count == 0)
            yield break;

        var lastTailDocument = tailDocuments
            .Select(c => new { Document = c, MaxEntryLid = c.Metadata.ChatEntries.Max(x => x.LocalId) })
            .OrderByDescending(c => c.MaxEntryLid)
            .Select(c => c.Document)
            .FirstOrDefault();

        DocumentBuilder? tailBuilder = null;

        // Preload document tails
        if (lastTailDocument is not null) {
            tailBuilder = new DocumentBuilder(lastTailDocument);
            var tailEntries = await documentEntriesLoader.LoadTailEntries(lastTailDocument, cancellationToken)
                .ConfigureAwait(false);
            tailBuilder.Entries.AddRange(tailEntries);
            tailBuilder.Dialog = await chatDialogFormatter.EntriesToText(tailEntries).ConfigureAwait(false);
            tailBuilder.WordCount = CountWords(tailBuilder.Dialog); // Incorrect way to count words. Take in account author name.
            tailBuilder.Embeddings = await CalculateEmbeddings(tailBuilder.Dialog).ConfigureAwait(false);
        }

        if (tailBuilder is not null && ShouldCompleteDoc(tailBuilder))
            tailBuilder = null;

        var wordLimitPerBlock = WordCountPerBlock;
        if (tailBuilder is not null)
            wordLimitPerBlock = WordCountPerBlock - (tailBuilder.WordCount % WordCountPerBlock);

        DebugLog?.Invoke("Start processing buffer");
        DocumentBuilder? builder = null;
        foreach (var entry in bufferedEntries) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;
            if (entry.IsSystemEntry)
                continue;

            builder ??= new DocumentBuilder(null) {
                HasModified = true
            };

            builder.Entries.Add(entry);
            builder.Dialog = await chatDialogFormatter.EntriesToText(builder.Entries).ConfigureAwait(false);
            builder.WordCount = CountWords(builder.Dialog);

            if (builder.WordCount > wordLimitPerBlock) {
                DebugLog?.Invoke("-> Block size is reached");
                builder.Embeddings = await CalculateEmbeddings(builder.Dialog).ConfigureAwait(false);
                if (tailBuilder is not null) {
                    DebugLog?.Invoke($"Comparing with tail doc. Doc size is {builder.Entries.Count}. Tail doc size is {tailBuilder.Entries.Count}");
                    if (ShouldMerge(builder, tailBuilder)) {
                        DebugLog?.Invoke("Current doc should be merged with tail doc");
                        tailBuilder.Entries.AddRange(builder.Entries);
                        tailBuilder.Dialog = await chatDialogFormatter.EntriesToText(tailBuilder.Entries)
                            .ConfigureAwait(false);
                        tailBuilder.Embeddings = await CalculateEmbeddings(tailBuilder.Dialog).ConfigureAwait(false);
                        tailBuilder.WordCount = CountWords(tailBuilder.Dialog);
                        tailBuilder.HasModified = true;

                        if (ShouldCompleteDoc(tailBuilder)) {
                            DebugLog?.Invoke($"Should complete merged doc. Doc size is {tailBuilder.Entries.Count}. Word count is {tailBuilder.WordCount}");
                            yield return new SourceEntries(null, null, tailBuilder.Entries);
                            tailBuilder = null;
                        }
                        else {
                            DebugLog?.Invoke("Go on building next doc block");
                        }
                    }
                    else {
                        DebugLog?.Invoke($"Current doc is different from tail doc. Returning tail doc. Doc size is {tailBuilder.Entries.Count}. Word count is {tailBuilder.WordCount}");
                        // Complete because dialog content has changed significantly.
                        yield return new SourceEntries(null, null, tailBuilder.Entries);
                        tailBuilder = builder;
                    }
                }
                else {
                    DebugLog?.Invoke("Keep current doc as tail");
                    tailBuilder = builder;
                }
                builder = null;
                DebugLog?.Invoke("<- Block size is reached");
            }

            wordLimitPerBlock = WordCountPerBlock;
            if (tailBuilder is not null)
                wordLimitPerBlock = WordCountPerBlock - (tailBuilder.WordCount % WordCountPerBlock);
        }

        DebugLog?.Invoke("End of buffer is reached");
        if (builder is not null && tailBuilder is not null) {
            builder.Embeddings = await CalculateEmbeddings(builder.Dialog).ConfigureAwait(false);
            DebugLog?.Invoke($"Comparing with tail doc. Doc size is {builder.Entries.Count}. Tail doc size is {tailBuilder.Entries.Count}");
            if (ShouldMerge(builder, tailBuilder)) {
                DebugLog?.Invoke("Current doc should be merged with tail doc");
                tailBuilder.Entries.AddRange(builder.Entries);
                yield return new SourceEntries(null, null, tailBuilder.Entries);
            }
            else {
                DebugLog?.Invoke("Current doc is different from tail doc");
                // Complete because dialog content has changed significantly.
                yield return new SourceEntries(null, null, tailBuilder.Entries);
                yield return new SourceEntries(null, null, builder.Entries);
            }
        }
        else {
            DebugLog?.Invoke("Processing last doc");
            var x = tailBuilder ?? builder;
            if (x is not null)
                yield return new SourceEntries(null, null, x.Entries);
        }
    }

    private bool ShouldMerge(DocumentBuilder builder, DocumentBuilder tailBuilder)
    {
        var shouldMerge = IsSimilar(builder.Embeddings, tailBuilder.Embeddings)
            || tailBuilder.WordCount < MinBlockLength
            || builder.WordCount < MinBlockLength;
        return shouldMerge;
    }

    private static bool ShouldCompleteDoc(DocumentBuilder builder)
    {
        const int maxWordCountPerDoc = 400;
        if (builder.WordCount > maxWordCountPerDoc)
            return true;

        if (builder.Entries.Count == 0)
            return false;

        var startTime = builder.Entries.Min(c => c.BeginsAt);
        var endTime = builder.Entries.Max(c => c.BeginsAt);
        if (startTime + TimeSpan.FromDays(1) < endTime)
            return true;

        return false;
    }

    private bool IsSimilar(double[] a, double[] b)
    {
        var similarity = embeddingsCalculator.CosineSimilarity(a, b);
        DebugLog?.Invoke("Similarity=" + similarity);
        return similarity > 0.9d;
    }

    private Task<double[]> CalculateEmbeddings(string dialog)
        => embeddingsCalculator.CalculateVector(dialog);

    public static int CountWords(string input)
    {
        int wordCount = 0;
        bool inWord = false;

        foreach (char c in input) {
            if (char.IsWhiteSpace(c)) {
                if (inWord) {
                    wordCount++;
                    inWord = false;
                }
            }
            else {
                if (!inWord)
                    inWord = true;
            }
        }

        if (inWord)
            wordCount++;

        return wordCount;
    }

    private class DocumentBuilder(ChatSlice? relatedChatSlice)
    {
        public bool HasModified { get; set; }
        public List<ChatEntry> Entries { get; } = [];
        public string Dialog { get; set; } = "";
        public int WordCount { get; set; }
        public double[] Embeddings { get; set; } = [];
    }
}
