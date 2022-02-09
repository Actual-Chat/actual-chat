namespace ActualChat.Chat.UnitTests;

public class MarkupParsingTest
{
    [Fact]
    public async Task BasicTest()
    {
        var text = "Мороз и солнце; день чудесный!";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        result.Parts[0].Should().Be(text);
    }

    [Fact]
    public async Task BasicTestWithMap()
    {
        var linearMap = new LinearMap(1f, 2f, 3f, 4f);
        var result = await new MarkupParser().Parse("Мороз и солнце; день чудесный!", linearMap);
        result.Parts.Should().HaveCount(5);
        result.Parts.Should().AllBeOfType<PlainTextPart>();
        result.Parts[0].Text.Should().Be("Мороз ");
        result.Parts[1].Text.Should().Be("и ");
        result.Parts[2].Text.Should().Be("солнце; ");
        result.Parts[3].Text.Should().Be("день ");
        result.Parts[4].Text.Should().Be("чудесный!");
    }

    [Fact]
    public async Task AutoLinkTest()
    {
        var text = "https://habr.com/ru/all/";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var linkPart = result.Parts[0].Should().BeOfType<LinkPart>().Subject;
        linkPart.Url.Should().Be(text);
        linkPart.Text.Should().Be(text);
    }

    [Fact]
    public async Task AutoLinkImageTest()
    {
        var text = "https://pravlife.org/sites/default/files/field/image/13_48.jpg";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var imagePart = result.Parts[0].Should().BeOfType<ImagePart>().Subject;
        imagePart.Url.Should().Be(text);
        imagePart.Text.Should().Be(text);
    }
}
