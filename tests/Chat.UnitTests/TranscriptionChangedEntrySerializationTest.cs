namespace ActualChat.Chat.UnitTests;

public sealed class TranscriptionChangedEntrySerializationTest
{
    [Fact]
    public void TranscriptionChangedEntryShouldRoundTripAsChatEntry()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        ChatEntry entry = new TranscriptionChangedEntry(ChatEntryId.New(chatId, 7), 3) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            IsTranscriptionOn = true,
        };

        // act
        var read = entry.PassThroughModernSerializers();

        // assert
        var transcriptionChanged = read.Should().BeOfType<TranscriptionChangedEntry>().Subject;
        transcriptionChanged.Id.Should().Be(entry.Id);
        transcriptionChanged.Version.Should().Be(3);
        transcriptionChanged.TargetAuthorId.Should().Be(author);
        transcriptionChanged.TargetAuthorName.Should().Be("John");
        transcriptionChanged.IsTranscriptionOn.Should().BeTrue();
    }

    [Fact]
    public void TranscriptionChangedEntryTagShouldBeInTheSystemRange()
    {
        // act
        var tag = ChatEntry.GetUnionTag(new TranscriptionChangedEntry());

        // assert
        tag.Should().NotBeNull();
        ChatEntry.IsSystemUnionTag(tag!.Value).Should().BeTrue();
        ChatEntry.GetUnionTagSince(tag.Value).Should().Be(new Version(2, 22));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TranscriptionChangedEntryShouldRoundTripThroughTheLegacyEnvelope(bool isTranscriptionOn)
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        var entry = new TranscriptionChangedEntry(ChatEntryId.New(chatId, 7)) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            IsTranscriptionOn = isTranscriptionOn,
        };

        // act
        var json = Serializers.SystemJson.Write(LegacySystemEntry.From(entry)!);
        var back = Serializers.SystemJson.Read<LegacySystemEntry>(json);

        // assert
        var option = back.Option.Should().BeOfType<LegacyTranscriptionChangedOption>().Subject;
        option.AuthorId.Should().Be(author);
        option.AuthorName.Should().Be("John");
        option.IsOn.Should().Be(isTranscriptionOn);
    }
}
