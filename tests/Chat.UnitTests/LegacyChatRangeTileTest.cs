namespace ActualChat.Chat.UnitTests;

public sealed class LegacyChatRangeTileTest
{
    [Fact]
    public void RenamedTileShouldRetainTheLegacyMessagePackBytes()
    {
        // arrange
        var legacy = NewLegacyTile();
        var options = MessagePackByteSerializer.DefaultOptions;
        var bytes = MessagePackSerializer.Serialize(legacy, options);

        // act
        var tile = MessagePackSerializer.Deserialize<ChatRangeTile>(bytes, options);
        var encoded = MessagePackSerializer.Serialize(tile, options);

        // assert
        tile.ConversationRanges.Should().Equal(legacy.ConversationLidRanges);
        encoded.Should().Equal(bytes);
    }

    [Fact]
    public void RenamedPropertyShouldReadAndWriteTheLegacyJsonMember()
    {
        // arrange
        var legacy = NewLegacyTile();
        ITextSerializer[] serializers = [SystemJsonSerializer.Default, NewtonsoftJsonSerializer.Default];

        foreach (var serializer in serializers) {
            // act
            var json = serializer.Write(legacy);
            var tile = serializer.Read<ChatRangeTile>(json);
            var encoded = serializer.Write(tile);
            var restored = serializer.Read<LegacyChatRangeTile>(encoded);

            // assert
            tile.ConversationRanges.Should().Equal(legacy.ConversationLidRanges);
            restored.ConversationLidRanges.Should().Equal(legacy.ConversationLidRanges);
            encoded.Should().Be(json);
        }
    }

    // Private methods

    private static LegacyChatRangeTile NewLegacyTile()
        => new(new(0, 1280), [new(10, 300)], [new(50, 200)], 150, null, 1280);

    // Nested types

    [DataContract, MessagePackObject]
    public sealed record LegacyChatRangeTile(
        [property: DataMember, Key(0)] Range<long> LidRange,
        [property: DataMember, Key(1)] Range<long>[] EntryLidRanges,
        [property: DataMember, Key(2)] Range<long>[] ConversationLidRanges,
        [property: DataMember, Key(3)] int MinCount,
        [property: DataMember, Key(4)] long? PreviousLidTileStart,
        [property: DataMember, Key(5)] long? NextLidTileStart);
}
