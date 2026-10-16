namespace ActualChat.Chat.UnitTests;

public sealed class HandRaisedEntrySerializationTest
{
    [Fact]
    public void HandRaisedEntryShouldRoundTripAsChatEntry()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        ChatEntry entry = new HandRaisedEntry(ChatEntryId.New(chatId, 7), 3) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            IsHandRaised = true,
        };

        // act
        var read = entry.PassThroughModernSerializers();

        // assert
        var handRaised = read.Should().BeOfType<HandRaisedEntry>().Subject;
        handRaised.Id.Should().Be(entry.Id);
        handRaised.Version.Should().Be(3);
        handRaised.TargetAuthorId.Should().Be(author);
        handRaised.TargetAuthorName.Should().Be("John");
        handRaised.IsHandRaised.Should().BeTrue();
    }

    [Fact]
    public void HandRaisedEntryTagShouldBeInTheSystemRange()
    {
        // act
        var tag = ChatEntry.GetUnionTag(new HandRaisedEntry());

        // assert
        tag.Should().NotBeNull();
        ChatEntry.IsSystemUnionTag(tag!.Value).Should().BeTrue();
        ChatEntry.GetUnionTagSince(tag.Value).Should().Be(new Version(2, 22));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandRaisedEntryShouldRoundTripThroughTheLegacyEnvelope(bool isHandRaised)
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var author = AuthorId.New(chatId, 1);
        var entry = new HandRaisedEntry(ChatEntryId.New(chatId, 7)) {
            TargetAuthorId = author,
            TargetAuthorName = "John",
            IsHandRaised = isHandRaised,
        };

        // act
        var json = Serializers.SystemJson.Write(LegacySystemEntry.From(entry)!);
        var back = Serializers.SystemJson.Read<LegacySystemEntry>(json);

        // assert
        var option = back.Option.Should().BeOfType<LegacyHandRaisedOption>().Subject;
        option.AuthorId.Should().Be(author);
        option.AuthorName.Should().Be("John");
        option.IsRaised.Should().Be(isHandRaised);
    }
}
