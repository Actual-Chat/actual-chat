namespace ActualChat.Chat.UnitTests;

public class HistoryChangedEntrySerializationTest
{
    [Fact]
    public void HistoryChangedEntryShouldRoundTripAsChatEntry()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        ChatEntry entry = new HistoryChangedEntry(ChatEntryId.New(chatId, 7), 3) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            HistoryChange = HistoryChangeKind.Wiped,
            HistoryPeriod = TimeSpan.FromDays(2),
        };

        // act
        var read = entry.PassThroughModernSerializers();

        // assert
        var historyChanged = read.Should().BeOfType<HistoryChangedEntry>().Subject;
        historyChanged.Id.Should().Be(entry.Id);
        historyChanged.Version.Should().Be(3);
        historyChanged.TargetAuthorId.Should().Be(author);
        historyChanged.TargetAuthorName.Should().Be("John");
        historyChanged.HistoryChange.Should().Be(HistoryChangeKind.Wiped);
        historyChanged.HistoryPeriod.Should().Be(TimeSpan.FromDays(2));
    }

    [Fact]
    public void HistoryChangedEntryTagShouldBeInTheSystemRange()
    {
        // act
        var tag = ChatEntry.GetUnionTag(new HistoryChangedEntry());

        // assert
        tag.Should().NotBeNull();
        ChatEntry.IsSystemUnionTag(tag!.Value).Should().BeTrue();
        ChatEntry.GetUnionTagSince(tag.Value).Should().Be(new Version(2, 22));
    }

    [Fact]
    public void HistoryChangedEntryShouldRoundTripThroughTheLegacyEnvelope()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        var entry = new HistoryChangedEntry(ChatEntryId.New(chatId, 7)) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            HistoryChange = HistoryChangeKind.RetentionChanged,
            HistoryPeriod = TimeSpan.FromHours(5),
        };

        // act
        var json = Serializers.SystemJson.Write(LegacySystemEntry.From(entry)!);
        var back = Serializers.SystemJson.Read<LegacySystemEntry>(json);

        // assert
        var option = back.Option.Should().BeOfType<LegacyHistoryChangedOption>().Subject;
        option.AuthorId.Should().Be(author);
        option.AuthorName.Should().Be("John");
        option.Change.Should().Be(HistoryChangeKind.RetentionChanged);
        option.Period.Should().Be(TimeSpan.FromHours(5));
    }
}
