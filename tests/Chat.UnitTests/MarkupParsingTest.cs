namespace ActualChat.Chat.UnitTests;

public class MarkupParsingTest
{
    [Fact]
    public async Task BasicTest()
    {
        var text = "Мороз и солнце; день чудесный!";
        var result = await new MarkupParser().Parse(text);
        var plainText = result.Parts[0].Should().BeOfType<PlainTextPart>().Subject;
        plainText.Text.Should().Be(text);
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

    [Fact]
    public async Task EmphasisTest()
    {
        var text = "*italic text*";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var textPart = result.Parts[0].Should().BeOfType<FormattedTextPart>().Subject;
        textPart.Emphasis.Should().Be(Emphasis.Em);
        textPart.Text.Should().Be("italic text");
    }

    [Fact]
    public async Task StrongTest()
    {
        var text = "**bold text**";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var textPart = result.Parts[0].Should().BeOfType<FormattedTextPart>().Subject;
        textPart.Emphasis.Should().Be(Emphasis.Strong);
        textPart.Text.Should().Be("bold text");
    }

    [Fact]
    public async Task EmphasisMixedTest()
    {
        var text = "***bold mixed*** text";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(2);
        var formattedTextPart = result.Parts[0].Should().BeOfType<FormattedTextPart>().Subject;
        formattedTextPart.Emphasis.Should().Be(Emphasis.Strong | Emphasis.Em);
        formattedTextPart.Text.Should().Be("bold mixed");
        var plainTextPart = result.Parts[1].Should().BeOfType<PlainTextPart>().Subject;
        plainTextPart.Text.Should().Be(" text");
    }

    [Fact]
    public async Task CodeTest()
    {
        var text = "Variables declaration: ```var x = 0;```";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(2);
        var plainTextPart = result.Parts[0].Should().BeOfType<PlainTextPart>().Subject;
        plainTextPart.Text.Should().Be("Variables declaration: ");
        var codePart = result.Parts[1].Should().BeOfType<CodePart>().Subject;
        codePart.Text.Should().Be("var x = 0;");
        codePart.Code.Should().Be("var x = 0;");
        codePart.IsInline.Should().BeTrue();
    }

    [Fact]
    public async Task CodeBlockTest()
    {
        var text = @"Small program example
```cs
var x = 0;
var y = x + 1;
```";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(2);
        var plainTextPart = result.Parts[0].Should().BeOfType<PlainTextPart>().Subject;
        plainTextPart.Text.Should().Be("Small program example");
        var codePart = result.Parts[1].Should().BeOfType<CodePart>().Subject;
        codePart.Language.Should().Be("cs");
        codePart.Code.Should().Be(@"var x = 0;
var y = x + 1;");
        codePart.Text.Should().Be(@"var x = 0;
var y = x + 1;");
        codePart.IsInline.Should().BeFalse();
    }

    [Fact]
    public async Task NewLineTest()
    {
        var text = "line 1\r\nline2";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var plainTextPart = result.Parts[0].Should().BeOfType<PlainTextPart>().Subject;
        plainTextPart.Text.Should().Be(text);
    }

    [Fact]
    public async Task SanitizeLinkTest()
    {
        var text = "[test](javascript:alert(123))";
        var result = await new MarkupParser().Parse(text);
        result.Parts.Length.Should().Be(1);
        var linkPart = result.Parts[0].Should().BeOfType<PlainTextPart>().Subject;
        linkPart.Text.Should().Be("test");
    }
}
