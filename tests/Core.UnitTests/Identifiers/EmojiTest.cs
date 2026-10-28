using System.Text;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Identifiers;

public class EmojiTest
{
    private static readonly MessagePackSerializerOptions MessagePackOptions = new(AppMessagePackResolver.Instance);
    private const string UnknownSymbol = "🦄";

    [Fact]
    public void Parse()
    {
        Emoji[] emojis = [
            // Positive
            Emojis.Lol,
            Emojis.Awesome,
            Emojis.Cool,
            Emojis.Smile,
            Emojis.Nerd,
            Emojis.SmileRotated,
            Emojis.Kiss,
            // Negative
            Emojis.Angry,
            Emojis.Sad,
            Emojis.Crying,
            Emojis.Melting,
            Emojis.Devil,
            Emojis.Clown,
            // Love
            Emojis.Love,
            Emojis.InLove,
            Emojis.BrokenHeart,
            // Gestures
            Emojis.ThumbsUp,
            Emojis.Done,
            Emojis.Eyes,
            Emojis.Poop,
            Emojis.ExplodingHead,
            Emojis.Surprise,
            Emojis.Mysterious,
            Emojis.StoneFaceMoai,
        ];

        foreach (var emoji in emojis) {
            var parsed = Emoji.TryParse(emoji.Value, out var emoji2);
            parsed.Should().BeTrue();
            emoji2.Should().Be(emoji);
        }
    }

    [Fact]
    public void ParseLegacyEmojis()
    {
        foreach (var emoji in Emojis.Legacy) {
            var parsed = Emoji.TryParse(emoji.Value, out var emoji2);
            parsed.Should().BeTrue();
            emoji2.Should().Be(emoji);
        }
    }

    [Fact]
    public void LegacyEmojisNotInAll()
    {
        foreach (var emoji in Emojis.Legacy)
            Emojis.All.Should().NotContain(emoji);
    }

    [Fact]
    public void ScaredIsScreamingFaceWithLegacyFearfulArtwork()
    {
        Emojis.Scared.Symbol.Should().Be("😱");
        Emojis.All.Should().Contain(Emojis.Scared);
        Emojis.TryGetSvgName(Emojis.Scared).Should().Be("emoji-scared");

        Emojis.Fearful.Symbol.Should().Be("😨");
        Emojis.Legacy.Should().Contain(Emojis.Fearful);
        Emojis.TryGetSvgName("😨").Should().Be("emoji-scared");
    }

    [Fact]
    public void UnknownEmojiShouldDeserializeAsPlaceholder()
    {
        // arrange
        var json = $"\"{UnknownSymbol}\"";

        // act
        var fromMessagePack = MessagePackSerializer.Deserialize<Emoji>(
            MessagePackSerializer.Serialize(UnknownSymbol, MessagePackOptions), MessagePackOptions);
        var fromSystemJson = new SystemJsonSerializer(JsonSerializerOptions.Default).Read<Emoji>(json);
        var fromNewtonsoftJson = new NewtonsoftJsonSerializer().Read<Emoji>(json);

        // assert
        fromMessagePack.Value.Should().Be(UnknownSymbol);
        fromMessagePack.Symbol.Should().Be(UnknownSymbol, "the UI shows an emoji without artwork as its symbol");
        Emojis.TryGetSvgName(fromMessagePack).Should().BeNull();
        fromSystemJson.Should().Be(fromMessagePack);
        fromNewtonsoftJson.Should().Be(fromMessagePack);
        Emoji.TryParse(UnknownSymbol).Should().BeNull("only deserialization tolerates unknown emojis");
        fromMessagePack.Invoking(x => x.RequireSupported()).Should().Throw<FormatException>();
    }

    [Fact]
    public void KnownEmojiShouldDeserializeAsItsSingleton()
    {
        // arrange
        var bytes = MessagePackSerializer.Serialize(Emojis.ThumbsUp, MessagePackOptions);

        // act
        var result = MessagePackSerializer.Deserialize<Emoji>(bytes, MessagePackOptions);

        // assert
        result.Should().BeSameAs(Emojis.ThumbsUp);
        result.RequireSupported().Should().BeSameAs(Emojis.ThumbsUp);
    }

    [Fact]
    public void UnknownEmojiShouldNotFailReactionPayloads()
    {
        // arrange
        var entryId = ChatEntryId.Parse("the-actual-one:0:7");
        var authorId = AuthorId.Parse("the-actual-one:1");
        ApiArray<ReactionSummary> summaries = [
            new() { Id = "1", EntryId = entryId, Emoji = Emojis.ThumbsUp, Count = 1 },
            new() { Id = "2", EntryId = entryId, Emoji = Emojis.Fire, Count = 2 },
        ];
        ApiArray<CallReaction> callReactions = [
            new(authorId, Emojis.ThumbsUp, Moment.EpochStart),
            new(authorId, Emojis.Fire, Moment.EpochStart),
        ];
        var summaryBytes = ReplaceFire(MessagePackSerializer.Serialize(summaries, MessagePackOptions));
        var callReactionBytes = ReplaceFire(MessagePackSerializer.Serialize(callReactions, MessagePackOptions));

        // act
        var summaries2 = MessagePackSerializer.Deserialize<ApiArray<ReactionSummary>>(summaryBytes, MessagePackOptions);
        var callReactions2 = MessagePackSerializer.Deserialize<ApiArray<CallReaction>>(
            callReactionBytes, MessagePackOptions);

        // assert
        summaries2.Select(x => x.Emoji.Value).Should().Equal(Emojis.ThumbsUp.Value, UnknownSymbol);
        summaries2[0].Emoji.Should().BeSameAs(Emojis.ThumbsUp);
        summaries2[1].Count.Should().Be(2);
        callReactions2.Select(x => x.Emoji.Value).Should().Equal(Emojis.ThumbsUp.Value, UnknownSymbol);
        callReactions2[1].Emoji.Should().Be(summaries2[1].Emoji, "placeholders of one code are equal");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("\n")]
    [InlineData("0123456789012345678901234567890123")]
    public void MalformedEmojiShouldStillFailDeserialization(string value)
    {
        // arrange
        var bytes = MessagePackSerializer.Serialize(value, MessagePackOptions);

        // act
        var deserialize = () => MessagePackSerializer.Deserialize<Emoji>(bytes, MessagePackOptions);

        // assert
        deserialize.Should().Throw<MessagePackSerializationException>();
    }

    // Private methods

    private static byte[] ReplaceFire(byte[] bytes)
    {
        var oldBytes = Encoding.UTF8.GetBytes(Emojis.Fire.Value);
        var newBytes = Encoding.UTF8.GetBytes(UnknownSymbol);
        newBytes.Length.Should().Be(oldBytes.Length);
        var index = bytes.AsSpan().IndexOf(oldBytes);
        index.Should().BeGreaterThanOrEqualTo(0);
        var result = bytes.ToArray();
        newBytes.CopyTo(result, index);
        return result;
    }
}
