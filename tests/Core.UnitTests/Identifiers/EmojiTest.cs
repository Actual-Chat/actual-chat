namespace ActualChat.Core.UnitTests.Identifiers;

public class EmojiTest
{
    [Fact]
    public void Parse()
    {
        Emoji[] emojis = [
            Emojis.ThumbsUp,
            Emojis.RedHeart,
            Emojis.Lol,
            Emojis.Surprise,
            Emojis.Sad,
            Emojis.Angry,
            Emojis.Poo,
        ];

        foreach (var emoji in emojis) {
            Emoji.TryParse(emoji.Value, out var emoji2);
            emoji2.Should().Be(emoji);
        }
    }
}
