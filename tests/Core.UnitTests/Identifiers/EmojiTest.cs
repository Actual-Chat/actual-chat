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
}
