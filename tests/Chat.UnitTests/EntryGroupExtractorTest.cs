using System.IO.Compression;
using ActualChat.Chat.ML;
using Newtonsoft.Json;

namespace ActualChat.Chat.UnitTests;

public class EntryGroupExtractorTest(ITestOutputHelper @out, ILogger<EntryGroupExtractor> log) : TestBase(@out, log)
{
    [Fact]
    public async Task ExtractGroups_EmptyEntries_ReturnsEmpty()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
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
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = new ExtractorState(null, null);
        var entries = new List<TextEntry> {
            new (0, "System message", null!, DateTime.Now, null, false, null)
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
            new (0, "Entry 1", null!, baseTime, null, false, null),
            new (0, "Entry 2", null!, baseTime.AddHours(1), null, false, null),
            new (0, "Entry 3", null!, baseTime.AddHours(13 + 1), null, false, null), // 13 hours after entry2
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

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
                null!,
                DateTime.Now.AddMinutes(i),
                null,
                false,
                null))
            .ToList();

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log, groupWordCount);

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
            .Select(i => new TextEntry(0, string.Join(" ", Enumerable.Repeat("word", 10)), null!, DateTime.Now.AddMinutes(i), null, false, null))
            .ToList();

        // Use a custom embeddings calculator with low similarity
        var lowSimilarityCalculator = new LowSimilarityEmbeddingsCalculator();
        var extractor = new EntryGroupExtractor(lowSimilarityCalculator, log, groupWordCount);

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
            new(1, "Entry 1", null!, DateTime.Now, null, false, null),
            new(2, "Reply to Entry 1", null!, DateTime.Now.AddSeconds(10), null, false, 1),
            new(3, "Entry 2", null!, DateTime.Now.AddMinutes(1), null, false, null)
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.ReplySequences.Should().BeEmpty();
        result.Groups.Should().BeEmpty();
        result.State.CurrentGroup.Should().NotBeNull();
        result.State.CurrentChunk.Should().NotBeNull();
        result.State.CurrentChunk!.Entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExtractGroups_ReplySequenceExceedsLimit_CompletesSequence()
    {
        // Arrange
        var entries = new List<TextEntry>
        {
            new(2, "Entry 1", null!, DateTime.Now, null, false, null),
            new(3, "Reply to Entry 1", null!, DateTime.Now.AddSeconds(10), null, false, 1),
            new(4, "Comment to reply", null!, DateTime.Now.AddSeconds(20), null, false, null),
            new(5, "Another comment", null!, DateTime.Now.AddSeconds(30), null, false, null),
            new(6, "Another comment 2", null!, DateTime.Now.AddSeconds(30), null, false, null),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

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
            new(2, "Entry 1", null!, DateTime.Now, null, false, null),
            new(3, "Reply to Entry 1", null!, DateTime.Now.AddSeconds(10), null, false, 1),
            new(4, "Late Reply", null!, DateTime.Now.AddMinutes(1), null, false, null),
        };

        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);

        // Act
        var result = await extractor.ExtractGroups(new ExtractorState(null, null), entries, CancellationToken.None);

        // Assert
        result.ReplySequences.Should().ContainSingle();
        var replySequence = result.ReplySequences[0];
        replySequence.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExtractGroups_ExtractGroupsFromRealData_ShouldProvideConversations()
    {
        // Arrange
        var extractor = new EntryGroupExtractor(new HighSimilarityEmbeddingsCalculator(), log);
        var initialState = new ExtractorState(null, null);
        var entries = await ReadTestEntries("./data/chat_entries.zip");

        // Act
        var result = await extractor.ExtractGroups(initialState, entries, CancellationToken.None);

        // Assert
        result.Groups.Should().NotBeEmpty();
        result.Groups.Count.Should().BeLessThan(60);
        result.Groups.Should().AllSatisfy(group => group.Entries.Should().NotBeEmpty());
    }

    [Fact(Skip = "For exploratory purposes only")]
    public async Task ExtractGroups_ExtractGroupsFromRealDataWithEmbeddings_ShouldProvideConversations()
    {
        // Arrange
        var settings = new EmbeddingSettings {
            // PredictionsUri = "http://localhost:28080/predictions/Alibaba-NLP_gte-multilingual-base",
            PredictionsUri = "http://localhost:8000/v1/embeddings", // Experimenting with another (much faster!) model served with vllm
        };
        var extractor = new EntryGroupExtractor(new EmbeddingsCalculator(settings), log);
        var initialState = new ExtractorState(null, null);
        var entries = await ReadTestEntries("./data/chat_entries.zip");

        // Act
        var result = await extractor.ExtractGroups(initialState, entries, CancellationToken.None);

        // Assert
        result.Groups.Should().NotBeEmpty();
        result.Groups.Count.Should().BeLessThan(50);
        result.Groups.Should().AllSatisfy(group => group.Entries.Should().NotBeEmpty());
    }


    private async Task<IReadOnlyCollection<TextEntry>> ReadTestEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.First();
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<List<JsonTextEntry>>(json)!
            .Select(jsonEntry => new TextEntry(
                jsonEntry.LocalId,
                jsonEntry.Content,
                AuthorId.Parse(jsonEntry.AuthorId),
                new Moment(jsonEntry.BeginsAt),
                jsonEntry.EndsAt != null ? new Moment(jsonEntry.EndsAt.Value) : null,
                jsonEntry.AudioEntryId != null,
                jsonEntry.RepliedChatEntryId))
            .ToList();
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
            => Task.FromResult(new[] { 0.3, 0.4 }); // Different from HighSimilarityEmbeddingsCalculator

        public double CosineSimilarity(double[] a, double[] b)
            => 0.85; // Below the 0.9 threshold

        public double[] Normalize(double[] vector)
            => vector;
    }

    public record JsonTextEntry(
        [property: JsonProperty("local_id")] long LocalId,
        [property: JsonProperty("author_id")] string AuthorId,
        [property: JsonProperty("begins_at")] DateTime BeginsAt,
        [property: JsonProperty("ends_at")] DateTime? EndsAt,
        [property: JsonProperty("duration")] double Duration,
        [property: JsonProperty("content")] string Content,
        [property: JsonProperty("audio_entry_id")] long? AudioEntryId,
        [property: JsonProperty("replied_chat_entry_id")] long? RepliedChatEntryId,
        [property: JsonProperty("is_system_entry")] bool IsSystemEntry
    );
}
