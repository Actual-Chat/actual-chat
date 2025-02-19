using ActualChat.Chat.ML;
using MemoryPack;

namespace ActualChat.Chat.UnitTests;

public class EntryGroupBuilderTest
{
    [Fact]
    public void AddEntry_IncreasesWordCount()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", AuthorId.None, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry);

        Assert.Equal(2, builder.WordCount);
    }

    [Fact]
    public void AddEntry_UpdatesAveragePauseBetweenEntries()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "First entry", AuthorId.None, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "Second entry", AuthorId.None, new Moment(DateTime.Now.AddSeconds(10)), null, false, null);

        builder.Add(entry1);
        builder.Add(entry2);

        Assert.Equal(10, builder.AveragePauseBetweenEntries);
    }

    [Fact]
    public void AddRange_AddsMultipleEntries()
    {
        var builder = new EntryGroupBuilder();
        var entries = new List<TextEntry> {
            new TextEntry(0, "First entry", AuthorId.None, new Moment(DateTime.Now), null, false, null),
            new TextEntry(0, "Second entry", AuthorId.None, new Moment(DateTime.Now), null, false, null),
        };

        builder.AddRange(entries);

        Assert.Equal(2, builder.Entries.Count);
    }

    [Fact]
    public void Text_ReturnsConcatenatedContent()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", AuthorId.None, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", AuthorId.None, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry1);
        builder.Add(entry2);

        Assert.Equal("Helloworld", builder.Text);
    }

    [Fact]
    public void Build_ReturnsEntryGroup()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", AuthorId.None, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry);
        var entryGroup = builder.Build();

        Assert.Equal(1, entryGroup.Entries.Count);
        Assert.Equal(2, entryGroup.WordCount);
    }

    [Fact]
    public void GetPauseBetween_ReturnsZeroForFirstEntry()
    {
        var builder = new EntryGroupBuilder();
        var entry = new TextEntry(0, "Hello world", AuthorId.None, new Moment(DateTime.Now), null, false, null);

        var pause = builder.GetPauseBetween(entry);

        Assert.Equal(0, pause);
    }

    [Fact]
    public void AddEntry_ResetsText()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", AuthorId.None, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", AuthorId.None, new Moment(DateTime.Now), null, false, null);

        builder.Add(entry1);
        var textBefore = builder.Text;
        builder.Add(entry2);

        Assert.NotEqual(textBefore, builder.Text);
    }

    [Fact]
    public void SerializeAndDeserialize_EntryGroupBuilder()
    {
        var builder = new EntryGroupBuilder();
        var entry1 = new TextEntry(0, "Hello", AuthorId.None, new Moment(DateTime.Now), null, false, null);
        var entry2 = new TextEntry(0, "world", AuthorId.None, new Moment(DateTime.Now), null, false, null);
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
