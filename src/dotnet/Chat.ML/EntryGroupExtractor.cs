using MemoryPack;

namespace ActualChat.Chat.ML;

public record EntryGroup(IReadOnlyList<TextEntry> Entries, int WordCount = 0, bool IsCompleted = false);

public record ReplySequence(IReadOnlyList<TextEntry> Entries);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExtractorState(
    [property: DataMember, MemoryPackOrder(0)]
    EntryGroupBuilder? CurrentGroup,
    [property: DataMember, MemoryPackOrder(1)]
    EntryGroupBuilder? CurrentChunk)
{
    [MemoryPackIgnore]
    public long MaxLid => Math.Max(CurrentChunk?.MaxLid ?? 0, CurrentGroup?.MaxLid ?? 0);
}

public record ExtractResult(ExtractorState State, IReadOnlyList<EntryGroup> Groups, IReadOnlyList<ReplySequence> ReplySequences);

public interface IEntryGroupExtractor
{
    ExtractResult ExtractGroups(
        ExtractorState? state,
        IReadOnlyCollection<TextEntry> entries);
}

public enum EntryGroupLimit
{
    None = 0,
    Small = 100,
    Medium = 400,
    Large = 2000,
}

public class EntryGroupExtractor(IEmbeddingsCalculator embeddingsCalculator, ILogger<EntryGroupExtractor> log, int? groupWordCount = null) : IEntryGroupExtractor
{
    private const int ChunkWordCount = 100;
    private const int MaxPauseBetweenEntries = 60 * 60 * 12; // 12 hours
    private const int MinPauseBetweenSpeechEntries = 30; // 30 seconds
    private const int MinPauseBetweenTextEntries = 5 * 60; // 5 minutes

    private IEmbeddingsCalculator EmbeddingsCalculator { get; } = embeddingsCalculator;
    private ILogger Log { get; } = log;

    public ExtractResult ExtractGroups(
        ExtractorState? state,
        IReadOnlyCollection<TextEntry> entries)
    {
        state ??= new ExtractorState(null, null);
        if (entries.Count == 0)
            return new ExtractResult(state, [], []);

        var groups = new List<EntryGroup>();
        var replySequences = new List<ReplySequence>();
        var replyBuilder = new EntryGroupBuilder();
        var groupBuilder = state.CurrentGroup ?? new EntryGroupBuilder();
        var chunkBuilder = state.CurrentChunk ?? new EntryGroupBuilder();
        var lastEntryLid = chunkBuilder.Entries.Count > 0
            ? chunkBuilder.MaxLid
            : groupBuilder.Entries.Count > 0
                ? groupBuilder.MaxLid
                : -1;
        foreach (var entry in entries.SkipWhile(e => e.LocalId <= lastEntryLid)) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;

            // Handle replies
            if (entry.RepliedEntryLid is { } repliedEntryLocalId)
                if (repliedEntryLocalId < chunkBuilder.MinLid) {
                    // The replied entry is from a previous group
                    if (replyBuilder.Entries.Count > 0) {
                        replySequences.Add(new ReplySequence(replyBuilder.Entries));
                        replyBuilder = new EntryGroupBuilder();
                    }
                    replyBuilder.Add(entry);
                    continue;
                }
            if (replyBuilder.Entries.Count is > 0 and <= 3) {
                var pauseAfterReply = replyBuilder.GetPauseBetween(entry);
                if (pauseAfterReply < MinPauseBetweenSpeechEntries)
                    replyBuilder.Add(entry);
                else {
                    // The pause after the reply is too long
                    replySequences.Add(new ReplySequence(replyBuilder.Entries));
                    replyBuilder = new EntryGroupBuilder();
                }
            }
            if (replyBuilder.Entries.Count >= 3) {
                // Complete the reply sequence if it's too long
                replySequences.Add(new ReplySequence(replyBuilder.Entries));
                replyBuilder = new EntryGroupBuilder();
            }

            // Complete the group if the pause between entries is too long
            if (chunkBuilder.Entries.Count > 0)
                CompleteGroupOnPause(chunkBuilder, entry);
            else if (groupBuilder.Entries.Count > 0)
                CompleteGroupOnPause(groupBuilder, entry);

            chunkBuilder.Add(entry);
            if (chunkBuilder.WordCount >= ChunkWordCount) {
                // Calculate embeddings for the chunk and group
                // NOTE(AK): Skip embedding calculation for now
                // chunkBuilder.Embeddings = await CalculateEmbeddings(chunkBuilder, cancellationToken).ConfigureAwait(false);
                // groupBuilder.Embeddings = await CalculateEmbeddings(groupBuilder, cancellationToken).ConfigureAwait(false);
                if (ShouldMerge(groupBuilder, chunkBuilder)) {
                    groupBuilder.AddRange(chunkBuilder.Entries);
                    chunkBuilder = new EntryGroupBuilder();
                }
                else {
                    // Complete the group
                    groups.Add(groupBuilder.Build());
                    groupBuilder = chunkBuilder;
                    chunkBuilder = new EntryGroupBuilder();
                }
            }

            // ReSharper disable once InvertIf
            if (groupBuilder.WordCount >= groupWordCount) {
                // Complete the group
                groups.Add(groupBuilder.Build());
                groupBuilder = chunkBuilder;
                chunkBuilder = new EntryGroupBuilder();
            }
        }

        var resultState = groupBuilder.WordCount > 0 || chunkBuilder.WordCount > 0
            ? new ExtractorState(groupBuilder, chunkBuilder)
            : new ExtractorState(null, null);
        return new ExtractResult(resultState, groups, replySequences);

        void CompleteGroupOnPause(EntryGroupBuilder groupBuilder1, TextEntry entry)
        {
            var currentPause = groupBuilder1.GetPauseBetween(entry);
            var minPause = entry.IsTranscript ? MinPauseBetweenSpeechEntries : MinPauseBetweenTextEntries;
            var significantlyLargerThanAverage = chunkBuilder.AveragePauseBetweenEntries * 10;
            if ((currentPause < minPause || currentPause < significantlyLargerThanAverage) && currentPause < MaxPauseBetweenEntries)
                return;

            // Complete the chunk if the pause between entries is too long
            groupBuilder.AddRange(chunkBuilder.Entries);
            groups.Add(groupBuilder.Build());
            chunkBuilder = new EntryGroupBuilder();
            groupBuilder = new EntryGroupBuilder();
        }
    }

    private async Task<double[]> CalculateEmbeddings(EntryGroupBuilder groupBuilder, CancellationToken cancellationToken)
    {
        if (groupBuilder.Embeddings.Length > 0)
            return groupBuilder.Embeddings;

        if (groupBuilder.WordCount < ChunkWordCount)
            return []; // Skip calculating embeddings for small chunks

        var text = groupBuilder.Text;
        try {
            return await EmbeddingsCalculator.CalculateVector(text, cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalError e) {
            Log.LogWarning(e, "Failed to calculate embeddings for text: {Text}", text[..Math.Min(20, text.Length)]);
            return [];
        }
    }

    private bool ShouldMerge(EntryGroupBuilder groupBuilder, EntryGroupBuilder chunkBuilder)
    {
        if (chunkBuilder.WordCount == 0)
            return false;

        const int longPause = 60 * 60; // 1 hour
        var pauseBetweenGroups = groupBuilder.GetPauseBetween(chunkBuilder.Entries[0]);
        var isChunkCloseToGroup = pauseBetweenGroups <= groupBuilder.AveragePauseBetweenEntries * 4;
        var authorActivityIntersection = GetAuthorIntersection(groupBuilder, chunkBuilder);
        var shouldMerge = groupBuilder.WordCount < ChunkWordCount
            || isChunkCloseToGroup
            || (authorActivityIntersection > 0.5d && pauseBetweenGroups < longPause);
            // Do not use embeddings for now
            // TODO(AK): Use lazy embeddings calculation for similarity check
            // || AreSimilar(groupBuilder.Embeddings, chunkBuilder.Embeddings);
        return shouldMerge;
    }

    private double GetAuthorIntersection(EntryGroupBuilder groupBuilder, EntryGroupBuilder chunkBuilder)
    {
        var groupActivity = groupBuilder.AuthorActivity;
        var chunkActivity = chunkBuilder.AuthorActivity;

        if (groupActivity.Count == 0 || chunkActivity.Count == 0)
            return 0.0;

        var intersectionWeight = 0.0;
        var total = groupActivity.Count + chunkActivity.Count;

        foreach (var author in groupActivity.Keys)
            if (chunkActivity.TryGetValue(author, out _))
                intersectionWeight++;

        return intersectionWeight * 2 / total;
    }

    private bool AreSimilar(double[] a, double[] b)
    {
        if (a.Length == 0 || b.Length == 0)
            return true;

        var similarity = EmbeddingsCalculator.CosineSimilarity(a, b);
        return similarity > 0.9d;
    }
}
