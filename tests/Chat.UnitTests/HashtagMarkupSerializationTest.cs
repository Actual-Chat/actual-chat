namespace ActualChat.Chat.UnitTests;

public class HashtagMarkupSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void PassesThroughMessagePack()
    {
        // arrange
        Markup markup = new HashtagMarkup("#promo");

        // act
        var result = markup.PassThroughModernSerializers(Out);

        // assert
        var hashtag = result.Should().BeOfType<HashtagMarkup>().Subject;
        hashtag.Text.Should().Be("#promo");
        hashtag.Tag.Should().Be("promo");
    }
}
