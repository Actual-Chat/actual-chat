namespace ActualChat.Chat.UnitTests;

public class MarkupParserTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void EmptyTextTest()
    {
        var m = Parse<ParagraphMarkup>("", out var text);
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void NewLineTest()
    {
        var m = Parse<MarkupSeq>(Environment.NewLine, out var text);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
    }

    [Fact]
    public void BasicTest()
    {
        var m = Parse<ParagraphMarkup>("123 456", out var text);
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        m = Parse<ParagraphMarkup>("123 _ 456", out text);
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        m = Parse<ParagraphMarkup>(" ", out text);
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void TwoLinesParagraphTest()
    {
        var m = Parse<MarkupSeq>("Мороз и солнце; день\r\n чудесный!");
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Мороз и солнце; день");
        m.Items[1].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be(" чудесный!");
    }

    [Fact]
    public void UrlTest()
    {
        var p = Parse<ParagraphMarkup>("https://habr.com/ru/all/", out var text);
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        p = Parse<ParagraphMarkup>("https://console.cloud.google.com/logs/query;query=resource.labels.container_name%3D%22actual-chat-app%22;timeRange=PT1H;summaryFields=:false:32:beginning:false;cursorTimestamp=2022-05-23T10:19:37.057723681Z?referrer=search&project=actual-chat-app-prod", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        p = Parse<ParagraphMarkup>("https://www.booking.com/hotel/gr/peninsula-agia-pelagia.html?label=gr-9DH6*qo6Fm", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        p = Parse<ParagraphMarkup>("https://www.roveconcepts.com/round-chair?aid[12]=173&aid[79]=724&weird=|this|", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        p = Parse<ParagraphMarkup>($"https://{Constants.Hosts.Voxt}/?ws=!1m4!1m3", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Theory]
    [InlineData("https://habr.com/ru/all/?q=1")]
    [InlineData("https://youtube.com/@some-channel?v=some-video-id")]
    [InlineData("https://en.wikipedia.org/wiki/Sampling_(signal_processing)")]
    public void UrlWithQueryTest(string input, string? expected = null)
    {
        expected ??= input;
        var p = Parse<ParagraphMarkup>(input, out var text);
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text).And.Be(expected);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void UrlWithQueryAndHashTest()
    {
        var p = Parse<ParagraphMarkup>("https://docs.google.com/spreadsheets/d/nj/edit#gid=1534300344 x");
        var m = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        m.Items.Length.Should().Be(2);
        var url = (UrlMarkup)m.Items[0];
        url.Url.Should().EndWith("344");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void UrlWithCommaInHashTest()
    {
        var p = Parse<ParagraphMarkup>("https://github.com/Actual-Chat/actual-chat/blob/710d73de02f1241e1f4b2e8c13e6f8978c3896c9/src/nodejs/styles/tailwind.css#L18,L23 x");
        var m = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        m.Items.Length.Should().Be(2);
        var url = (UrlMarkup)m.Items[0];
        url.Url.Should().EndWith("L18,L23");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void UrlWithQuoteInQuery()
    {
        var url = $"https://{Constants.Hosts.Voxt}?k='v'";
        var p = Parse<ParagraphMarkup>($"{url} x");
        var m = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        m.Items.Length.Should().Be(2);
        var urlMarkup = (UrlMarkup)m.Items[0];
        urlMarkup.Url.Should().Be($"https://{Constants.Hosts.Voxt}?k='v'");
        var text = (PlainTextMarkup)m.Items[1];
        text.Text.Should().Be(" x");
    }

    [Fact]
    public void MentionTest()
    {
        var p = Parse<ParagraphMarkup>("@a:abcdef:1", out var text);
        var m = p.Content.Should().BeOfType<MentionMarkup>().Subject;
        m.Id.Value.Should().Be(text[1..]);

        p = Parse<ParagraphMarkup>("@u:userId", out text);
        m = p.Content.Should().BeOfType<MentionMarkup>().Subject;
        m.Id.Value.Should().Be(text[1..]);

        Parse<ParagraphMarkup>("@alex", out text);
        Parse<ParagraphMarkup>("@ something", out text);
    }

    [Fact]
    public void NamedMentionTest()
    {
        var p = Parse<ParagraphMarkup>("@`a`a:chatid:1");
        var m = p.Content.Should().BeOfType<MentionMarkup>().Subject;
        m.Name.Should().Be("a");
        m.Id.Value.Should().Be("a:chatid:1");

        p = Parse<ParagraphMarkup>("@`a x`a:chatid:1");
        m = p.Content.Should().BeOfType<MentionMarkup>().Subject;
        m.Name.Should().Be("a x");
        m.Id.Value.Should().Be("a:chatid:1");

        // Empty id case
        Parse<ParagraphMarkup>("@`Alex Yakunin`");
        Parse<ParagraphMarkup>("@`a`b");
    }

    [Fact]
    public void ImageTest()
    {
        var p = Parse<ParagraphMarkup>("https://pravlife.org/sites/field/image/13_48.jpg", out var text);
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);

        p = Parse<ParagraphMarkup>("www.pravlife.org/sites/field/image/13_48.jpg", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Fact]
    public void EmailTest()
    {
        var p = Parse<ParagraphMarkup>("whatever@gmail.com", out var text);
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Email);

        p = Parse<ParagraphMarkup>("mailto:whatever@gmail.com", out text);
        m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Email);
    }

    [Fact]
    public void ItalicTest()
    {
        var p = Parse<ParagraphMarkup>("*italic text*", out var text);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Style.Should().Be(TextStyle.Italic);
        var m1 = m.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m1.Text.Should().Be(text[1..^1]);
    }

    [Fact]
    public void BoldTest()
    {
        var p = Parse<ParagraphMarkup>("**bold text**", out var text);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Style.Should().Be(TextStyle.Bold);
        var m1 = m.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m1.Text.Should().Be(text[2..^2]);
    }

    [Fact]
    public void PreformattedTest()
    {
        var p = Parse<ParagraphMarkup>("`a``b`");
        var m = p.Content.Should().BeOfType<PreformattedTextMarkup>().Subject;
        m.Text.Should().Be("a`b");
    }

    [Fact]
    public void CodeBlockTest()
    {
        var m = Parse<CodeBlockMarkup>(@"```cs
code
```");
        m.Language.Should().Be("cs");
        m.Code.Should().Be("code\r\n");

        m = Parse<CodeBlockMarkup>("``` code\n```", false);
        m.Language.Should().Be("");
        m.Code.Should().Be("code\r\n");

        m = Parse<CodeBlockMarkup>(@"```cs
```");
        m.Language.Should().Be("cs");
        m.Code.Should().Be("");

        m = Parse<CodeBlockMarkup>(@"```cs
    public class CodeWithIndent
    {
      Test();
    }
```", false);
        m.Language.Should().Be("cs");
        m.Code.Should().Be(@"public class CodeWithIndent
{
  Test();
}
".Replace("\n", "\r\n", StringComparison.OrdinalIgnoreCase));

        m = Parse<CodeBlockMarkup>(@"```cs

    public class CodeWithIndent
    {
    }

```", false);
        m.Language.Should().Be("cs");
        m.Code.Should().Be(@"
public class CodeWithIndent
{
}

".Replace("\n", "\r\n", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MixedCodeTest()
    {
        var m = Parse<MarkupSeq>(@"
1
```cs
code
```
2");
        m.Items.Length.Should().Be(4);
        m.Items[0].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[1].Should().BeOfType<ParagraphMarkup>();
        m.Items[2].Should().BeOfType<CodeBlockMarkup>();
        m.Items[3].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void ComplexMixedCodeTest()
    {
        var m = Parse<MarkupSeq>(@"
*1* **
```cs
code
```
2 ```cs");
        m.Items.Length.Should().Be(4);
        m.Items[0].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[1].Should().BeOfType<ParagraphMarkup>();
        m.Items[2].Should().BeOfType<CodeBlockMarkup>();
        m.Items[3].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void UnparsedTest()
    {
        var p = Parse<ParagraphMarkup>("**", out var text);
        var m = p.Content.Should().BeOfType<UnparsedTextMarkup>().Subject;
        m.Text.Should().Be(text);
    }

    [Fact]
    public void MixedTest()
    {
        var p = Parse<ParagraphMarkup>("***bi*** @alex `a``b` *i* **b** *");
        var m = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        m.Items.Length.Should().Be(11);
        var um = (UnparsedTextMarkup)m.Items.Last();
        um.Text.Should().Be("*");
    }

    [Fact]
    public void SpecialTest_CssRuleCase()
    {
        var p = Parse<ParagraphMarkup>("--background-message-hover: #f3f4f6;", out var text);
        var m = p.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m.Text.Should().Be(text);
    }

    [Fact]
    public void SpecialTest_SmileCase()
    {
        var p = Parse<ParagraphMarkup>(":)", out var text);
        var m = p.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m.Text.Should().Be(text);
    }


    [Fact]
    public void SpecialTest_DoubleSmileCase()
    {
        var p = Parse<ParagraphMarkup>(":) :)", out var text);
        var m = p.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m.Text.Should().Be(text);
    }

    [Fact]
    public void SpecialTest_MultilineCase()
    {
        var m = Parse<MarkupSeq>("line1 \nline2", out _);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line1 ");
        m.Items[1].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line2");
    }

    [Fact]
    public void UnorderedListCase()
    {
        var m = Parse<ListMarkup>("- line1\n- line2", out _);
        m.Items.Length.Should().Be(2);
        m.Items[0].Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("line1");
        m.Items[1].Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("line2");
    }

    [Fact]
    public void UnorderedListWithMentionsCase()
    {
        var text =
            """
            - Participants @a:chatxyz:3, @a:chatxyz:2 and @a:chatxyz:1 exchanged greetings and discussed the status of current tasks.
            - @a:chatxyz:2 raised questions about adding and managing the list of images.
            """;
        var m = Parse<ListMarkup>(text);
        m.Items.Length.Should().Be(2);
        var chatId = ChatId.Parse("chatxyz");

        var item0 = m.Items[0].Content.Should().BeOfType<MarkupSeq>().Subject;
        item0.Items.Length.Should().Be(7);
        item0.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("Participants ");
        item0.Items[1].Should().BeOfType<MentionMarkup>().Which.Id.Should().Be(MentionId.NewAuthor(AuthorId.New(chatId, 3)));
        item0.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(", ");
        item0.Items[3].Should().BeOfType<MentionMarkup>().Which.Id.Should().Be(MentionId.NewAuthor(AuthorId.New(chatId, 2)));
        item0.Items[4].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" and ");
        item0.Items[5].Should().BeOfType<MentionMarkup>().Which.Id.Should().Be(MentionId.NewAuthor(AuthorId.New(chatId, 1)));
        item0.Items[6].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" exchanged greetings and discussed the status of current tasks.");

        var item1 = m.Items[1].Content.Should().BeOfType<MarkupSeq>().Subject;
        item1.Items.Length.Should().Be(2);
        item1.Items[0].Should().BeOfType<MentionMarkup>().Which.Id.Should().Be(MentionId.NewAuthor(AuthorId.New(chatId, 2)));
        item1.Items[1].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" raised questions about adding and managing the list of images.");
    }

    [Fact]
    public void CodeBlockWithExtraText()
    {
        var text =
            """
            Text before the code block.
            ```
            Code block
            is here
            ```
            Text after the code block.
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text before the code block.");
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text after the code block.");
        m.Items[1].Should().BeOfType<CodeBlockMarkup>()
            .Which.Code.Should().Be("Code block\r\nis here\r\n");
    }

    [Fact]
    public void UnorderedListSurroundedWithText()
    {
        var text =
            """
            Text before the list.
            - List item 1 is here.
            - List item 2 is here.
            - List item 3 is the last one.
            Text after the list.
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text before the list.");
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text after the list.");

        var item1 = m.Items[1].Should().BeOfType<ListMarkup>().Subject;
        item1.Items.Should().HaveCount(3);
        item1.Items[0].Should().BeOfType<ListItemMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("List item 1 is here.");
        item1.Items[1].Should().BeOfType<ListItemMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("List item 2 is here.");
        item1.Items[2].Should().BeOfType<ListItemMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("List item 3 is the last one.");
    }

    [Fact]
    public void UnorderedListAndCodeBlockSurroundedWithText()
    {
        var text =
            """
            Text before the list.
            - List item 1 is here.
            - List item 2 is here.
            ```
            some code
            ```
            Text after the list.
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(4);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text before the list.");
        m.Items[3].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text after the list.");

        var item1 = m.Items[1].Should().BeOfType<ListMarkup>().Subject;
        item1.Items.Should().HaveCount(2);
        item1.Items[0].Should().BeOfType<ListItemMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("List item 1 is here.");
        item1.Items[1].Should().BeOfType<ListItemMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("List item 2 is here.");

        m.Items[2].Should().BeOfType<CodeBlockMarkup>()
            .Which.Code.Should().Be("some code\r\n");
    }

    [Fact]
    public void KeepNewLineAfterListBlock()
    {
        var text =
            """
            - List item 1 is here.
            - List item 2 is here.

            """;

        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(2);
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
    }

    [Fact]
    public void KeepNewLineAfterCodeBlock()
    {
        var text =
            """
            ```
            some code block
            ```

            """;

        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<CodeBlockMarkup>();
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
    }

    [Fact]
    public void ListBlockWithNewLineSeparator()
    {
        var text =
            """
            **Text before the list.**
            - List item 1 is here.
            - List item 2 is here.

            **Text after the list.**
            - List item 3 is here.
            - List item 4 is here.
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(5);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Bold);
        m.Items[1].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(2);
        m.Items[2].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[3].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Bold);
        m.Items[4].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(2);
    }

    [Fact]
    public void TwoListsAndCodeBlock()
    {
        var text =
            """
            **header 1**
            - line 1
            - line 2

            *header 2**
            - line 3
            - line 4

            ```
            some code block
            ```
            """;
        var m = Parse<MarkupSeq>(text);
    }

    [Fact]
    public void TwoListsAndCodeBlock2()
    {
        var text =
            """
            **header 1**
            - line 1
            - line 2


            *header 2**
            - line 3
            - line 4


            ```
            some code block
            ```
            """;
        var m = Parse<MarkupSeq>(text);
    }

    [Fact]
    public void BlockCodeWithNewLineAfter()
    {
        var text =
            """
            ```
            some code block
            ```

            """;
        var m = Parse<MarkupSeq>(text);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("hello world", true)]
    [InlineData("hello world 123", true)]
    [InlineData("line1\nline2", true)]
    [InlineData("line1\r\nline2", true)]
    [InlineData("  spaces  ", true)]
    [InlineData("@a:chatid:1", false)] // mention
    [InlineData("hello @a:chatid:1 world", false)] // mention in text
    [InlineData("https://example.com", false)] // URL
    [InlineData("hello https://example.com world", false)] // URL in text
    [InlineData("**bold**", false)] // bold
    [InlineData("*italic*", false)] // italic
    [InlineData("`code`", false)] // preformatted
    [InlineData("hello someone@email.com", false)] // email
    public void IsPlainTextTest(string input, bool expected)
    {
        var parser = new MarkupParser();
        var markup = parser.Parse(input);
        markup.IsPlainText().Should().Be(expected,
            $"markup type is {markup.GetType().Name} for input: \"{input}\"");
    }

    // Helpers

    private TResult Parse<TResult>(string text, out string copy)
        where TResult : Markup
        => Parse<TResult>(text, true, out copy);

    private TResult Parse<TResult>(string text, bool validateFormat, out string copy)
        where TResult : Markup
    {
        copy = text;
        return Parse<TResult>(text, validateFormat);
    }

    private TResult Parse<TResult>(string text, bool validateFormat = true)
        where TResult : Markup
    {
        WriteLine($"Input:");
        WriteLine($"  \"{text}\"");
        WriteLine("");
        WriteLine("Parsing:");
        ParserExt.DebugOutput = line => WriteLine(line);
        var parsed = MarkupParser.ParseRaw(text, true);
        var simplified = parsed.Simplify();
        WriteLine("");
        WriteLine("Output:");
        WriteLine($"  {simplified}");
        WriteLine($"  Raw: {parsed}");
        var result = simplified.Should().BeOfType<TResult>().Subject;
        if (validateFormat) {
            var expectedMarkupText = text.OrdinalReplace("\r\n", "\n");
            var markupText1 = simplified.Format().OrdinalReplace("\r\n", "\n");
            var markupText2 = MarkupFormatter.Default.Format(simplified).OrdinalReplace("\r\n", "\n");
            markupText1.Should().Be(expectedMarkupText);
            markupText2.Should().Be(expectedMarkupText);
        }
        return result;
    }
}
