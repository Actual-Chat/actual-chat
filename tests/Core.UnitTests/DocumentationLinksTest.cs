namespace ActualChat.Core.UnitTests;

public class DocumentationLinksTest
{
    [Theory]
    [InlineData(null, "https://docs.voxt.ai/?lang=")]
    [InlineData("en-US", "https://docs.voxt.ai/?lang=en")]
    [InlineData("ru-RU", "https://docs.voxt.ai/?lang=ru")]
    [InlineData("pt-BR", "https://docs.voxt.ai/?lang=pt")]
    [InlineData("nl-NL", "https://docs.voxt.ai/?lang=nl")]
    public void DocumentationShouldIncludeOnlyExplicitLanguage(string? language, string expected)
    {
        // act
        var url = Links.Documentation(language is null ? null : Language.Parse(language));

        // assert
        url.Should().Be(expected);
    }
}
