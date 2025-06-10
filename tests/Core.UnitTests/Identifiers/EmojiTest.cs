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
            Emojis.OkHand,
            Emojis.Fire,
            Emojis.BeamingFace,
            Emojis.ThumbsDown,
            Emojis.ScreamingFaceInFear,
            Emojis.JackOLantern,
            Emojis.FramedPicture,
        ];

        foreach (var emoji in emojis) {
            Emoji.TryParse(emoji.Value, out var emoji2);
            emoji2.Should().Be(emoji);
        }
    }
}
