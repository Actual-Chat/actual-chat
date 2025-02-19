using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests;

public class EntryGroupExtractorTest
{
    [Fact]
    public async Task ExtractGroups_EmptyEntries_ReturnsEmpty()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());
        var initialState = new ExtractorState(null, null);

        // Act
        var result = await extractor.ExtractGroups(initialState, [], CancellationToken.None);

        // Assert
        result.Groups.Should().BeEmpty();
        result.ReplySequences.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractGroups_SystemEntry_IsIgnored()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());
        var initialState = new ExtractorState(null, null);
        var entries = new List<TextEntry>
        {
            new (0, "System message", default, DateTime.Now, null, false, null)
        };

        // Act
        var result = await extractor.ExtractGroups(initialState, entries, CancellationToken.None);

        // Assert
        result.Groups.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractGroups_PauseExceedsMax_CompletesGroup()
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0);
        var entries = new List<TextEntry>
        {
            new (0, "Entry 1", default, baseTime, null, false, null),
            new (0, "Entry 2", default, baseTime.AddHours(1), null, false, null),
            new (0, "Entry 3", default, baseTime.AddHours(13 + 1), null, false, null), // 13 hours after entry2
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.Groups.Should().ContainSingle();
        var group = result.Groups[0];
        group.Entries.Should().HaveCount(2);
        group.Entries[0].Content.Should().Be(entries[0].Content);
        group.Entries[1].Content.Should().Be(entries[1].Content);

        // Check state: currentChunk contains the third entry
        result.State.CurrentChunk.Should().NotBeNull();
        result.State.CurrentChunk!.Entries.Should().ContainSingle();
        result.State.CurrentChunk.Entries[0].Content.Should().Be(entries[2].Content);
    }

    [Fact]
    public async Task ExtractGroups_ChunkFillsAndMerges_CompletesGroupWhenWordCountMet()
    {
        // Arrange
        var groupWordCount = 100;
        var entries = Enumerable.Range(0, 10)
            .Select(i => new TextEntry(
                0,
                string.Join(" ", Enumerable.Repeat("word", 10)),
                default,
                DateTime.Now.AddMinutes(i),
                null,
                false,
                null))
            .ToList();

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), groupWordCount);

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.Groups.Should().ContainSingle();
        result.Groups[0].Entries.Should().HaveCount(10);
    }

    [Fact]
    public async Task ExtractGroups_ChunkNotMergedDueToLowSimilarity_CompletesGroup()
    {
        // Arrange
        var groupWordCount = 100;
        var entries = Enumerable.Range(0, 10)
            .Select(i => new TextEntry(0, string.Join(" ", Enumerable.Repeat("word", 10)), default, DateTime.Now.AddMinutes(i), null, false, null))
            .ToList();

        // Use a custom embeddings calculator with low similarity
        var lowSimilarityCalculator = new LowSimilarityEmbeddingsCalculator();
        var extractor = new EntryGroupExtractor(lowSimilarityCalculator, groupWordCount);

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.Groups.Should().ContainSingle();
        result.Groups[0].Entries.Should().HaveCount(10);
    }

    [Fact]
    public async Task ExtractGroups_ReplySequenceWithinGroup_IsNotCaptured()
    {
        // Arrange
        var entries = new List<TextEntry>
        {
            new(1, "Entry 1", default, DateTime.Now, null, false, null),
            new(2, "Reply to Entry 1", default, DateTime.Now.AddSeconds(10), null, false, 1),
            new(3, "Entry 2", default, DateTime.Now.AddMinutes(1), null, false, null)
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.ReplySequences.Should().BeEmpty();
        result.Groups.Should().BeEmpty();
        result.State.CurrentGroup.Should().NotBeNull();
        result.State.CurrentChunk.Should().NotBeNull();
        result.State.CurrentChunk!.Entries.Should().HaveCount(3);
    }

    private static ChatEntryId BuildEntryId(int lid)
        => new (new ChatId("testchatid"), ChatEntryKind.Text, lid, AssumeValid.Option);

    [Fact]
    public async Task ExtractGroups_ReplySequenceExceedsLimit_CompletesSequence()
    {
        // Arrange
        var entries = new List<TextEntry>
        {
            new(2, "Entry 1", default, DateTime.Now, null, false, null),
            new(3, "Reply to Entry 1", default, DateTime.Now.AddSeconds(10), null, false, 1),
            new(4, "Comment to reply", default, DateTime.Now.AddSeconds(20), null, false, null),
            new(5, "Another comment", default, DateTime.Now.AddSeconds(30), null, false, null),
            new(6, "Another comment 2", default, DateTime.Now.AddSeconds(30), null, false, null),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.ReplySequences.Should().ContainSingle();
        var replySequence = result.ReplySequences[0];
        replySequence.Entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExtractGroups_ReplySequenceWithLongPause_CompletesSequence()
    {
        // Arrange
        var entries = new List<TextEntry>
        {
            new(2, "Entry 1", default, DateTime.Now, null, false, null),
            new(3, "Reply to Entry 1", default, DateTime.Now.AddSeconds(10), null, false, 1),
            new(4, "Late Reply", default, DateTime.Now.AddMinutes(1), null, false, null),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator());

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.ReplySequences.Should().ContainSingle();
        var replySequence = result.ReplySequences[0];
        replySequence.Entries.Should().HaveCount(1);
    }

    private class HighSimilarityEmbeddingsCalculator : IEmbeddingsCalculator
    {
        public Task<double[]> CalculateVector(string text, CancellationToken cancellationToken)
            => Task.FromResult(new [] { 0.1, 0.2 });

        public double CosineSimilarity(double[] a, double[] b)
            => 0.95;

        public double[] Normalize(double[] vector)
            => vector;
    }

    private class LowSimilarityEmbeddingsCalculator : IEmbeddingsCalculator
    {
        public Task<double[]> CalculateVector(string text, CancellationToken cancellationToken)
            => Task.FromResult(new double[] { 0.3, 0.4 }); // Different from HighSimilarityEmbeddingsCalculator

        public double CosineSimilarity(double[] a, double[] b)
            => 0.85; // Below the 0.9 threshold

        public double[] Normalize(double[] vector)
            => vector;
    }
}
