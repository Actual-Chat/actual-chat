using ActualChat.Chat.ML;
using MemoryPack;

namespace ActualChat.Chat.UnitTests;

public class EntryGroupBuilderTest
{
    [Fact]
    public void AddEntry_IncreasesWordCount()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", null!, new Moment(DateTime.Now), null, false, null);
        builder.Add(entry);
        builder.WordCount.Should().Be(2);
    }

    [Fact]
    public void AddEntry_UpdatesAveragePauseBetweenEntries()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "First entry", null!, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "Second entry", null!, new Moment(DateTime.Now.AddSeconds(10)), null, false, null);

        builder.Add(entry1);
        builder.Add(entry2);

        builder.AveragePauseBetweenEntries.Should().Be(10);
    }

    [Fact]
    public void AddRange_AddsMultipleEntries()
    {
        var builder = new EntryGroupBuilder();
        var entries = new List<TextEntry> {
            new (0, "First entry", null!, new Moment(DateTime.Now), null, false, null),
            new (0, "Second entry", null!, new Moment(DateTime.Now), null, false, null),
        };

        builder.AddRange(entries);

        builder.Entries.Count.Should().Be(2);
    }

    [Fact]
    public void Text_ReturnsConcatenatedContent()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", null!, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", null!, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry1);
        builder.Add(entry2);

        builder.Text.Should().Be("Hello\nworld\n");
    }

    [Fact]
    public void Build_ReturnsEntryGroup()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", null!, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry);
        var entryGroup = builder.Build();

        entryGroup.Entries.Count.Should().Be(1);
        entryGroup.WordCount.Should().Be(2);
    }

    [Fact]
    public void GetPauseBetween_ReturnsZeroForFirstEntry()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", null!, new Moment(DateTime.Now), null, false, null);

        var pause = builder.GetPauseBetween(entry);

        pause.Should().Be(0);
    }

    [Fact]
    public void AddEntry_ResetsText()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", null!, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", null!, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry1);
        var textBefore = builder.Text;
        builder.Add(entry2);

        builder.Text.Should().NotBe(textBefore);
    }

    [Fact]
    public void SerializeAndDeserialize_EntryGroupBuilder()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", null!, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", null!, new Moment(DateTime.Now), null, false, null);
        builder.Add(entry1);
        builder.Add(entry2);

        // Serialize
        var serializedData = MemoryPackSerializer.Serialize(builder);

        // Deserialize
        var deserializedBuilder = MemoryPackSerializer.Deserialize<EntryGroupBuilder>(serializedData);

        // Assert
        deserializedBuilder!.Entries.Count.Should().Be(builder.Entries.Count);
        deserializedBuilder.WordCount.Should().Be(builder.WordCount);
        deserializedBuilder.Text.Should().Be(builder.Text);
    }
}
