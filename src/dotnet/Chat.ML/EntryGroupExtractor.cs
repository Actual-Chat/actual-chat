using MemoryPack;

namespace ActualChat.Chat.ML;

public record EntryGroup(IReadOnlyList<TextEntry> Entries, int WordCount = 0, bool IsCompleted = false);

public record ReplySequence(IReadOnlyList<TextEntry> Entries);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ExtractorState(
    [property: DataMember, MemoryPackOrder(0)] EntryGroupBuilder? CurrentGroup,
    [property: DataMember, MemoryPackOrder(1)] EntryGroupBuilder? CurrentChunk);

public record ExtractResult(ExtractorState State, IReadOnlyList<EntryGroup> Groups, IReadOnlyList<ReplySequence> ReplySequences);

public interface IEntryGroupExtractor
{
    Task<ExtractResult> ExtractGroups(
        ExtractorState? state,
        IReadOnlyCollection<TextEntry> entries,
        CancellationToken cancellationToken);
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

    public async Task<ExtractResult> ExtractGroups(
        ExtractorState? state,
        IReadOnlyCollection<TextEntry> entries,
        CancellationToken cancellationToken)
    {
        state ??= new ExtractorState(null, null);
        if (entries.Count == 0)
            return new ExtractResult(state, Array.Empty<EntryGroup>(), Array.Empty<ReplySequence>());

        var groups = new List<EntryGroup>();
        var replySequences = new List<ReplySequence>();
        var replyBuilder = new EntryGroupBuilder();
        var groupBuilder = state.CurrentGroup ?? new EntryGroupBuilder();
        var chunkBuilder = state.CurrentChunk ?? new EntryGroupBuilder();

        foreach (var entry in entries) {
            if (string.IsNullOrWhiteSpace(entry.Content))
                continue;

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
                if (pauseAfterReply < 30)
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

            if (chunkBuilder.Entries.Count > 1) {
                // Complete the group if the pause between entries is too long
                var currentPause = chunkBuilder.GetPauseBetween(entry);
                var minPause = entry.IsTranscript ? MinPauseBetweenSpeechEntries : MinPauseBetweenTextEntries;
                if ((currentPause > minPause && currentPause > chunkBuilder.AveragePauseBetweenEntries * 10) || currentPause > MaxPauseBetweenEntries) {
                    groupBuilder.AddRange(chunkBuilder.Entries);
                    groups.Add(groupBuilder.Build());
                    chunkBuilder = new EntryGroupBuilder();
                    groupBuilder = new EntryGroupBuilder();
                }
            }

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

        var pauseBetweenGroups = groupBuilder.GetPauseBetween(chunkBuilder.Entries[0]);
        var isChunkCloseToGroup = pauseBetweenGroups <= groupBuilder.AveragePauseBetweenEntries * 3;
        var authorActivityIntersection = GetAuthorActivityIntersection(groupBuilder, chunkBuilder);
        var shouldMerge = groupBuilder.WordCount < ChunkWordCount
            || isChunkCloseToGroup
            || authorActivityIntersection > 0.3d;
            // Do not use embeddings for now
            // TODO(AK): Use lazy embeddings calculation for similarity check
            // || AreSimilar(groupBuilder.Embeddings, chunkBuilder.Embeddings);
        return shouldMerge;
    }

    private double GetAuthorActivityIntersection(EntryGroupBuilder groupBuilder, EntryGroupBuilder chunkBuilder)
    {
        var groupActivity = groupBuilder.AuthorActivity;
        var chunkActivity = chunkBuilder.AuthorActivity;

        if (groupActivity.Count == 0 || chunkActivity.Count == 0)
            return 0.0;

        var intersectionWeight = 0.0;
        var totalWeight = groupActivity.Values.Sum() + chunkActivity.Values.Sum();

        foreach (var author in groupActivity.Keys)
            if (chunkActivity.TryGetValue(author, out var chunkWeight))
                intersectionWeight += Math.Min(groupActivity[author], chunkWeight);

        return intersectionWeight * 2 / totalWeight;
    }

    private bool AreSimilar(double[] a, double[] b)
    {
        if (a.Length == 0 || b.Length == 0)
            return true;

        var similarity = EmbeddingsCalculator.CosineSimilarity(a, b);
        return similarity > 0.9d;
    }
}
