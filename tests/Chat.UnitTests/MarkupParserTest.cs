namespace ActualChat.Chat.UnitTests;

public class MarkupParserTest : TestBase
{
    public MarkupParserTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void BasicTest()
    {
        var m = Parse<PlainTextMarkup>("123 456", out var text);
        m.Text.Should().Be(text);

        m = Parse<PlainTextMarkup>("123 _ 456", out text);
        m.Text.Should().Be(text);

        m = Parse<PlainTextMarkup>(" ", out text);
        m.Text.Should().Be(text);

        m = Parse<PlainTextMarkup>("", out text);
        m.Text.Should().Be(text);
    }

    [Fact]
    public void NewLineTest()
    {
        var m = Parse<MarkupSeq>("Мороз и солнце; день\r\n чудесный!", out var text);
        m.Items.Count(m => m is PlainTextMarkup).Should().Be(2);
        m.Items.Count(m => m is NewLineMarkup).Should().Be(1);
    }

    [Fact]
    public void UrlTest()
    {
        var m = Parse<UrlMarkup>("https://habr.com/ru/all/", out var text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        m = Parse<UrlMarkup>("https://console.cloud.google.com/logs/query;query=resource.labels.container_name%3D%22actual-chat-app%22;timeRange=PT1H;summaryFields=:false:32:beginning:false;cursorTimestamp=2022-05-23T10:19:37.057723681Z?referrer=search&project=actual-chat-app-prod", out text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void UrlWithQueryTest()
    {
        var m = Parse<UrlMarkup>("https://habr.com/ru/all/?q=1", out var text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void UrlWithQueryAndHashTest()
    {
        var m = Parse<MarkupSeq>("https://docs.google.com/spreadsheets/d/nj/edit#gid=1534300344 x", out _);
        m.Items.Length.Should().Be(2);
        var url = (UrlMarkup)m.Items[0];
        url.Url.Should().EndWith("344");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void UrlWithCommaInHashTest()
    {
        var m = Parse<MarkupSeq>("https://github.com/Actual-Chat/actual-chat/blob/710d73de02f1241e1f4b2e8c13e6f8978c3896c9/src/nodejs/styles/tailwind.css#L18,L23 x", out _);
        m.Items.Length.Should().Be(2);
        var url = (UrlMarkup)m.Items[0];
        url.Url.Should().EndWith("L18,L23");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void UrlWithQuoteInQuery()
    {
        var url = "https://actual.chat?k='v'";
        var m = Parse<MarkupSeq>($"{url} x", out _);
        m.Items.Length.Should().Be(2);
        var urlMarkup = (UrlMarkup)m.Items[0];
        urlMarkup.Url.Should().Be("https://actual.chat?k='v'");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void MentionTest()
    {
        var m = Parse<MentionMarkup>("@a:abcdef:1", out var text);
        m.Id.Value.Should().Be(text[1..]);

        m = Parse<MentionMarkup>("@u:userId", out text);
        m.Id.Value.Should().Be(text[1..]);

        Parse<MarkupSeq>("@alex", out text);
        Parse<MarkupSeq>("@ something", out text);
    }

    [Fact]
    public void NamedMentionTest()
    {
        var m = Parse<MentionMarkup>("@`a`a:chatid:1", out var text);
        m.Name.Should().Be("a");
        m.Id.Value.Should().Be("a:chatid:1");

        m = Parse<MentionMarkup>("@`a x`a:chatid:1", out text);
        m.Name.Should().Be("a x");
        m.Id.Value.Should().Be("a:chatid:1");

        // Empty id case
        Parse<MarkupSeq>("@`Alex Yakunin`", out text);
        Parse<MarkupSeq>("@`a`b", out text);
    }

    [Fact]
    public void ImageTest()
    {
        var m = Parse<UrlMarkup>("https://pravlife.org/sites/field/image/13_48.jpg", out var text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Image);

        m = Parse<UrlMarkup>("www.pravlife.org/sites/field/image/13_48.jpg", out text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Image);
    }


    [Fact]
    public void EmailTest()
    {
        var m = Parse<UrlMarkup>("whatever@gmail.com", out var text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Email);

        m = Parse<UrlMarkup>("mailto:whatever@gmail.com", out text);
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Email);
    }

    [Fact]
    public void ItalicTest()
    {
        var m = Parse<StylizedMarkup>("*italic text*", out var text);
        m.Style.Should().Be(TextStyle.Italic);
        var m1 = m.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m1.Text.Should().Be(text[1..^1]);
    }

    [Fact]
    public void BoldTest()
    {
        var m = Parse<StylizedMarkup>("**bold text**", out var text);
        m.Style.Should().Be(TextStyle.Bold);
        var m1 = m.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m1.Text.Should().Be(text[2..^2]);
    }

    [Fact]
    public void PreformattedTest()
    {
        var m = Parse<PreformattedTextMarkup>("`a``b`", out _);
        m.Text.Should().Be("a`b");
    }

    [Fact]
    public void CodeTest()
    {
        var m = Parse<CodeBlockMarkup>(@"```cs
code
```", out _);
        m.Language.Should().Be("cs");
        m.Code.Should().Be("code\r\n");

        m = Parse<CodeBlockMarkup>(@"```cs
```", out _);
        m.Language.Should().Be("cs");
        m.Code.Should().Be("");
    }

    [Fact]
    public void MixedCodeTest()
    {
        var m = Parse<MarkupSeq>(@"
1
```cs
code
```
2", out _);
        m.Items.Length.Should().Be(6);
    }

    [Fact]
    public void ComplexMixedCodeTest()
    {
        var m = Parse<MarkupSeq>(@"
*1* **
```cs
code
```
2 ```cs", out _);
        m.Items.Length.Should().Be(10);
    }

    [Fact]
    public void UnparsedTest()
    {
        var m = Parse<UnparsedTextMarkup>("**", out var text);
        m.Text.Should().Be(text);
    }

    [Fact]
    public void MixedTest()
    {
        var m = Parse<MarkupSeq>("***bi*** @alex `a``b` *i* **b** *", out _);
        m.Items.Length.Should().Be(11);
        var um = (UnparsedTextMarkup)m.Items.Last();
        um.Text.Should().Be("*");
    }

    [Fact]
    public void SpecialTest_CssRuleCase()
    {
        var m = Parse<PlainTextMarkup>("--background-message-hover: #f3f4f6;", out var text);
        m.Text.Should().Be(text);
    }

    [Fact]
    public void SpecialTest_SmileCase()
    {
        var m = Parse<PlainTextMarkup>(":)", out var text);
        m.Text.Should().Be(text);
    }


    [Fact]
    public void SpecialTest_DoubleSmileCase()
    {
        var m = Parse<PlainTextMarkup>(":) :)", out var text);
        m.Text.Should().Be(text);
    }

    [Fact]
    public void SpecialTest_MultilineCase()
    {
        var m = Parse<MarkupSeq>("line1 \nline2", out var text);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("line1 ");
        m.Items[1].Should().Be(NewLineMarkup.Instance);
        m.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("line2");
    }

    // Helpers

    private TResult Parse<TResult>(string text, out string copy)
        where TResult : Markup
    {
        copy = text;
        Out.WriteLine($"Input:");
        Out.WriteLine($"  \"{text}\"");
        ParserExt.DebugOutput = line => Out.WriteLine(line);
        var parsed = MarkupParser.ParseRaw(text, true);
        var simplified = parsed.Simplify();
        Out.WriteLine("Output:");
        Out.WriteLine($"  {simplified}");
        Out.WriteLine($"  Raw: {parsed}");
        var result = simplified.Should().BeOfType<TResult>().Subject;
        var expectedMarkupText = text.OrdinalReplace("\r\n", "\n");
        var markupText1 = simplified.Format().OrdinalReplace("\r\n", "\n");
        var markupText2 = MarkupFormatter.Default.Format(simplified).OrdinalReplace("\r\n", "\n");
        markupText1.Should().Be(expectedMarkupText);
        markupText2.Should().Be(expectedMarkupText);
        return result;
    }
}
