namespace ActualChat.Mui.UnitTests;

public class DigestInputTest
{
    [Fact]
    public void ParseChatIdsSplitsAndTrims()
    {
        // arrange
        var text = " abcdefghij ,klmnopqrst,, ";

        // act
        var chatIds = DigestInput.ParseChatIds(text);

        // assert
        chatIds.Select(x => x.Value).Should().Equal("abcdefghij", "klmnopqrst");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ")]
    public void ParseChatIdsReturnsEmptyForBlankText(string? text)
        => DigestInput.ParseChatIds(text).Should().BeEmpty();

    [Fact]
    public void ParseChatIdsRejectsInvalidIds()
    {
        // arrange
        var text = "abcdefghij, !!";

        // act
        var parse = () => DigestInput.ParseChatIds(text);

        // assert
        parse.Should().Throw<Exception>();
    }
}
