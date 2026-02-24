namespace ActualChat.Core.UnitTests.Identifiers;

public class EmojiTest
{
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
            Emojis.Please,
            // Gestures
            Emojis.ThumbsUp,
            Emojis.Done,
            Emojis.Eyes,
            Emojis.Poop,
            Emojis.Boom,
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
}
