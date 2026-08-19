using System.Collections.Concurrent;

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
        // A single newline creates one paragraph with NewLineMarkup inside
        var m = Parse<ParagraphMarkup>(Environment.NewLine, false);
        m.Content.Should().BeOfType<NewLineMarkup>();
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
        // Single newline keeps lines in the same paragraph with NewLineMarkup between them
        var m = Parse<ParagraphMarkup>("Мороз и солнце; день\r\n чудесный!", false);
        var seq = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[0].Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Мороз и солнце; день");
        seq.Items[1].Should().BeOfType<NewLineMarkup>();
        seq.Items[2].Should().BeOfType<PlainTextMarkup>()
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

    [Theory]
    [InlineData("https://example.com:8080/x")]
    [InlineData("http://localhost:5005")]
    [InlineData("https://example.com:65535/a?b=c")]
    [InlineData("www.example.com:8443/a")]
    public void ParsesUrlWithExplicitPort(string input)
    {
        // act
        var p = Parse<ParagraphMarkup>(input, out var text);

        // assert
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("www.example.com/x")]
    public void ParsesUrlWithoutPort(string input)
    {
        // act
        var p = Parse<ParagraphMarkup>(input, out var text);

        // assert
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Www);
    }

    [Theory]
    [InlineData("https://example.com:65536/x")]
    [InlineData("https://example.com:99999")]
    [InlineData("https://example.com:123456/x")]
    [InlineData("https://example.com:0")]
    [InlineData("https://example.com:")]
    [InlineData("https://example.com:1١")] // Arabic-Indic digit one
    [InlineData("https://example.com:٨٠")]
    public void DoesNotParseUrlWithInvalidPort(string input)
    {
        // Only ASCII 1..65535 counts as a port; anything else leaves the whole token as plain text.

        // act
        var p = Parse<ParagraphMarkup>(input, out var text);

        // assert
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
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
        var m = p.Content.Should().BeAssignableTo<MentionMarkup>().Subject;
        m.Id.Value.Should().Be(text[1..]);

        p = Parse<ParagraphMarkup>("@u:userId", out text);
        m = p.Content.Should().BeAssignableTo<MentionMarkup>().Subject;
        m.Id.Value.Should().Be(text[1..]);

        Parse<ParagraphMarkup>("@alex", out text);
        Parse<ParagraphMarkup>("@ something", out text);
    }

    [Fact]
    public void NamedMentionTest()
    {
        var p = Parse<ParagraphMarkup>("@`a`a:chatid:1");
        var m = p.Content.Should().BeAssignableTo<MentionMarkup>().Subject;
        m.Name.Should().Be("a");
        m.Id.Value.Should().Be("a:chatid:1");

        p = Parse<ParagraphMarkup>("@`a x`a:chatid:1");
        m = p.Content.Should().BeAssignableTo<MentionMarkup>().Subject;
        m.Name.Should().Be("a x");
        m.Id.Value.Should().Be("a:chatid:1");

        // Empty id case
        Parse<ParagraphMarkup>("@`Alex Yakunin`");
        Parse<ParagraphMarkup>("@`a`b");
    }

    [Fact]
    public void UniversalMentionKindsTest()
    {
        var p = Parse<ParagraphMarkup>("@a:chatid:1", out _);
        var m = p.Content.Should().BeOfType<AuthorMention>().Subject;
        m.Id.Kind.Should().Be(MentionKind.Author);
        m.AuthorId.Should().BeOfType<AuthorId>();

        p = Parse<ParagraphMarkup>("@u:userId", out _);
        var um = p.Content.Should().BeOfType<UserMention>().Subject;
        um.Id.Kind.Should().Be(MentionKind.User);
        um.UserId.Should().BeOfType<UserId>();

        p = Parse<ParagraphMarkup>("@c:abcdef1234", out _);
        var cm = p.Content.Should().BeOfType<ChatMention>().Subject;
        cm.Id.Kind.Should().Be(MentionKind.Chat);
        cm.ChatId.Should().BeOfType<GroupChatId>();

        p = Parse<ParagraphMarkup>("@p:abcdef1234", out _);
        var pm = p.Content.Should().BeOfType<PlaceMention>().Subject;
        pm.Id.Kind.Should().Be(MentionKind.Place);
        pm.PlaceId.Should().BeOfType<PlaceId>();

        p = Parse<ParagraphMarkup>("@e:smile", out _);
        var em = p.Content.Should().BeOfType<EmojiMention>().Subject;
        em.Id.Kind.Should().Be(MentionKind.Emoji);
        em.EmojiRef.Should().BeOfType<EmojiRef>();
    }

    [Fact]
    public void UnknownPrefixIsNotAMentionTest()
    {
        // 'z' isn't a registered prefix — the token shouldn't parse as a mention.
        Parse<ParagraphMarkup>("@z:foo");
        var ok = MentionRef.TryParse("z:foo", out var mention);
        ok.Should().BeFalse();
        mention.Should().BeNull();
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
    public void BoldWithNewLineInsideTest()
    {
        var p = Parse<ParagraphMarkup>("**bold \r\n text**", out var text);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Style.Should().Be(TextStyle.Bold);
        var m1 = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        m1.Items.Length.Should().Be(3);
        m1.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("bold ");
        m1.Items[1].Should().BeOfType<NewLineMarkup>();
        m1.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" text");
    }

    [Fact]
    public void SpoilerTest()
    {
        var p = Parse<ParagraphMarkup>("||spoiler text||", out var text);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Style.Should().Be(TextStyle.Spoiler);
        var m1 = m.Content.Should().BeOfType<PlainTextMarkup>().Subject;
        m1.Text.Should().Be(text[2..^2]);
    }

    [Fact]
    public void SpoilerWithBoldInsideTest()
    {
        var p = Parse<ParagraphMarkup>("||**bold** hidden||", out _);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Style.Should().Be(TextStyle.Spoiler);
        var seq = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<StylizedMarkup>().Which.Style.Should().Be(TextStyle.Bold);
    }

    [Fact]
    public void SinglePipeIsLiteralTest()
    {
        var m = MarkupParser.ParseRaw("a | b").Simplify();
        m.ToReadableText().Should().Be("a | b");
    }

    [Fact]
    public void UnterminatedSpoilerIsLiteralTest()
    {
        var m = MarkupParser.ParseRaw("a ||b c").Simplify();
        m.ToReadableText().Should().Be("a ||b c");
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
        m.Code.Should().Be("code");

        m = Parse<CodeBlockMarkup>("``` code\n```", false);
        m.Language.Should().Be("");
        m.Code.Should().Be("code");

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
}".Replace("\n", "\r\n", StringComparison.OrdinalIgnoreCase));

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
        // Leading newline becomes part of first paragraph (as NewLineMarkup)
        var m = Parse<MarkupSeq>(@"
1
```cs
code
```
2");
        m.Items.Length.Should().Be(3);
        // First paragraph contains NewLine + "1"
        var firstPara = m.Items[0].Should().BeOfType<ParagraphMarkup>().Subject;
        var firstParaContent = firstPara.Content.Should().BeOfType<MarkupSeq>().Subject;
        firstParaContent.Items[0].Should().BeOfType<NewLineMarkup>();
        firstParaContent.Items[1].Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("1");
        m.Items[1].Should().BeOfType<CodeBlockMarkup>();
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("2");
    }

    [Fact]
    public void ComplexMixedCodeTest()
    {
        // Leading newline becomes part of first paragraph (as NewLineMarkup)
        var m = Parse<MarkupSeq>(@"
*1* **
```cs
code
```
2 ```cs");
        m.Items.Length.Should().Be(3);
        // First paragraph contains NewLine + "*1* **"
        var firstPara = m.Items[0].Should().BeOfType<ParagraphMarkup>().Subject;
        firstPara.Content.Should().BeOfType<MarkupSeq>().Which.Items[0].Should().BeOfType<NewLineMarkup>();
        m.Items[1].Should().BeOfType<CodeBlockMarkup>();
        m.Items[2].Should().BeOfType<ParagraphMarkup>();
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
        // A hex color is an unavoidable hashtag false positive; the round-trip text stays exact.
        var p = Parse<ParagraphMarkup>("--background-message-hover: #f3f4f6;", out _);
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("--background-message-hover: ");
        seq.Items[1].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#f3f4f6");
        seq.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(";");
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
        // Single newline keeps lines in the same paragraph with NewLineMarkup between them
        var m = Parse<ParagraphMarkup>("line1 \nline2", false);
        var seq = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[0].Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line1 ");
        seq.Items[1].Should().BeOfType<NewLineMarkup>();
        seq.Items[2].Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line2");
    }

    [Fact]
    public void EmptyLineSeparatesParagraphs()
    {
        // Empty line (double newline) separates paragraphs
        var m = Parse<MarkupSeq>("line1\n\nline2", false);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line1");
        m.Items[1].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("line2");
    }

    [Fact]
    public void ParagraphBeforeList()
    {
        // List block terminates the preceding paragraph
        var m = Parse<MarkupSeq>("text\n- item", false);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("text");
        m.Items[1].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(1);
    }

    [Fact]
    public void ParagraphBeforeCodeBlock()
    {
        // CodeBlock terminates the preceding paragraph
        var text = "text\n```\ncode\n```";
        var m = Parse<MarkupSeq>(text, false);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("text");
        m.Items[1].Should().BeOfType<CodeBlockMarkup>();
    }

    [Fact]
    public void MultiLineParagraphWithFormatting()
    {
        // Multiple lines with inline formatting stay in the same paragraph
        var m = Parse<ParagraphMarkup>("**bold**\n*italic*", false);
        var seq = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Length.Should().Be(3);
        seq.Items[0].Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Bold);
        seq.Items[1].Should().BeOfType<NewLineMarkup>();
        seq.Items[2].Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Italic);
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
        item0.Items[1].Should().BeAssignableTo<MentionMarkup>().Which.Id.Should().Be(MentionRef.NewAuthor(AuthorId.New(chatId, 3)));
        item0.Items[2].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(", ");
        item0.Items[3].Should().BeAssignableTo<MentionMarkup>().Which.Id.Should().Be(MentionRef.NewAuthor(AuthorId.New(chatId, 2)));
        item0.Items[4].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" and ");
        item0.Items[5].Should().BeAssignableTo<MentionMarkup>().Which.Id.Should().Be(MentionRef.NewAuthor(AuthorId.New(chatId, 1)));
        item0.Items[6].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" exchanged greetings and discussed the status of current tasks.");

        var item1 = m.Items[1].Content.Should().BeOfType<MarkupSeq>().Subject;
        item1.Items.Length.Should().Be(2);
        item1.Items[0].Should().BeAssignableTo<MentionMarkup>().Which.Id.Should().Be(MentionRef.NewAuthor(AuthorId.New(chatId, 2)));
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
            .Which.Code.Should().Be("Code block\r\nis here");
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
            .Which.Code.Should().Be("some code");
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
        // An empty line after a list block is preserved as an empty ParagraphMarkup
        var text =
            """
            **Text before the list.**
            - List item 1 is here.
            - List item 2 is here.

            **Text after the list.**
            - List item 3 is here.
            - List item 4 is here.
            """;
        var m = Parse<MarkupSeq>(text, false);
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
    public void KeepEmptyLineBetweenListAndParagraph()
    {
        // An empty line between a list and a following paragraph must not be swallowed
        var text =
            """
            - List item 1 is here.
            - List item 2 is here.

            Text after the list.
            """;
        var m = Parse<MarkupSeq>(text, false);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<ListMarkup>().Which.Items.Should().HaveCount(2);
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text after the list.");
    }

    [Fact]
    public void KeepEmptyLineBetweenCodeBlockAndParagraph()
    {
        // Same behavior for code blocks
        var text =
            """
            ```
            some code
            ```

            Text after the code block.
            """;
        var m = Parse<MarkupSeq>(text, false);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Be("some code");
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Text after the code block.");
    }

    [Fact]
    public void TwoListsAndCodeBlock()
    {
        // Empty lines between blocks are consumed as separators (round-trip not guaranteed)
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
        var m = Parse<MarkupSeq>(text, false);
    }

    [Fact]
    public void TwoListsAndCodeBlock2()
    {
        // Each extra newline between blocks is preserved as a ParagraphMarkup.Empty,
        // so the input round-trips losslessly. Two blank lines = two EmptyParas.
        var text =
            """
            **header 1**
            - line 1
            - line 2


            **header 2**
            - line 3
            - line 4


            ```
            some code block
            ```
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(9);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Bold);
        m.Items[1].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(2);
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().Be(Markup.EmptyText);
        m.Items[3].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().Be(Markup.EmptyText);
        m.Items[4].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<StylizedMarkup>()
            .Which.Style.Should().Be(TextStyle.Bold);
        m.Items[5].Should().BeOfType<ListMarkup>()
            .Which.Items.Should().HaveCount(2);
        m.Items[6].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().Be(Markup.EmptyText);
        m.Items[7].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().Be(Markup.EmptyText);
        m.Items[8].Should().BeOfType<CodeBlockMarkup>();
    }

    [Theory]
    [InlineData("# Heading", 1)]
    [InlineData("## Heading", 2)]
    [InlineData("### Heading", 3)]
    public void HeaderLevelTest(string text, int expectedLevel)
    {
        var m = Parse<HeaderMarkup>(text);
        m.Level.Should().Be(expectedLevel);
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("Heading");
    }

    [Fact]
    public void HeaderTooManyHashesIsParagraph()
    {
        // 4+ hashes are not valid headers - should fall back to paragraph
        var m = Parse<ParagraphMarkup>("#### Not a header");
        m.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("#### Not a header");
    }

    [Fact]
    public void HeaderRequiresWhitespaceAfterHashes()
    {
        // No space after # means it's not a header (it's a hashtag now)
        var m = Parse<ParagraphMarkup>("#NotAHeader");
        m.Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#NotAHeader");
    }

    [Fact]
    public void HeaderWithBoldContent()
    {
        var m = Parse<HeaderMarkup>("# **Bold heading**");
        m.Level.Should().Be(1);
        var stylized = m.Content.Should().BeOfType<StylizedMarkup>().Subject;
        stylized.Style.Should().Be(TextStyle.Bold);
        stylized.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be("Bold heading");
    }

    [Fact]
    public void HeaderWithMention()
    {
        var m = Parse<HeaderMarkup>("# Welcome @u:userId");
        m.Level.Should().Be(1);
        var seq = m.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Should().HaveCount(2);
        seq.Items[1].Should().BeAssignableTo<MentionMarkup>();
    }

    [Fact]
    public void HeaderFollowedByParagraph()
    {
        var text =
            """
            # Heading
            Paragraph text.
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(1);
        m.Items[1].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Paragraph text.");
    }

    [Fact]
    public void ParagraphFollowedByHeader()
    {
        var text =
            """
            Some text.
            # Heading
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Some text.");
        m.Items[1].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(1);
    }

    [Fact]
    public void MultipleHeaders()
    {
        var text =
            """
            # Title
            ## Subtitle
            ### Section
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(1);
        m.Items[1].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(2);
        m.Items[2].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(3);
    }

    [Fact]
    public void KeepEmptyLineAfterHeader()
    {
        var text =
            """
            # Heading

            paragraph
            """;
        var m = Parse<MarkupSeq>(text, false);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<HeaderMarkup>();
        m.Items[1].Should().BeOfType<ParagraphMarkup>().Which.Content.Should().Be(Markup.EmptyText);
        m.Items[2].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("paragraph");
    }

    [Fact]
    public void HeaderAndListAndCodeBlock()
    {
        var text =
            """
            # Heading
            - item 1
            - item 2
            ```
            some code
            ```
            """;
        var m = Parse<MarkupSeq>(text);
        m.Items.Length.Should().Be(3);
        m.Items[0].Should().BeOfType<HeaderMarkup>().Which.Level.Should().Be(1);
        m.Items[1].Should().BeOfType<ListMarkup>().Which.Items.Should().HaveCount(2);
        m.Items[2].Should().BeOfType<CodeBlockMarkup>().Which.Code.Should().Be("some code");
    }

    [Fact]
    public void HeaderIsBlockMarkup()
    {
        var m = Parse<HeaderMarkup>("# Title");
        ((Markup)m).Should().BeAssignableTo<BlockMarkup>();
        m.IsBlockMarkup().Should().BeTrue();
    }

    [Fact]
    public void UnclosedCodeBlockExtendsToEndOfMessage()
    {
        var text =
            """
            ```
            some code
            more code
            """;

        var m = Parse<CodeBlockMarkup>(text, false);
        m.Code.Should().Be("some code\r\nmore code");
        m.Language.Should().BeEmpty();
    }

    [Fact]
    public void UnclosedCodeBlockWithLanguage()
    {
        var text =
            """
            ```csharp
            var x = 1;
            """;

        var m = Parse<CodeBlockMarkup>(text, false);
        m.Code.Should().Be("var x = 1;");
        m.Language.Should().Be("csharp");
    }

    [Fact]
    public void UnclosedCodeBlockAfterParagraph()
    {
        var text =
            """
            Some intro text.
            ```
            unclosed code
            spanning to end
            """;

        var m = Parse<MarkupSeq>(text, false);
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().BeOfType<PlainTextMarkup>()
            .Which.Text.Should().Be("Some intro text.");
        m.Items[1].Should().BeOfType<CodeBlockMarkup>()
            .Which.Code.Should().Be("unclosed code\r\nspanning to end");
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
        m.Items.Length.Should().Be(2);
        m.Items[0].Should().BeOfType<CodeBlockMarkup>()
            .Which.Code.Should().Be("some code block");
        m.Items[1].Should().BeOfType<ParagraphMarkup>()
            .Which.Content.Should().Be(Markup.EmptyText);
    }

    [Theory]
    [InlineData("A\nB")]                          // 0 blanks, inline newline in one paragraph
    [InlineData("A\n\nB")]                        // 1 blank between paragraphs
    [InlineData("A\n\n\nB")]                      // 2 blanks
    [InlineData("A\n\n\n\nB")]                    // 3 blanks
    [InlineData("A\n\n\n\n\nB")]                  // 4 blanks
    [InlineData("A\n```\ncode\n```")]             // paragraph then adjacent code block
    [InlineData("A\n\n```\ncode\n```")]           // paragraph + 1 blank + code block
    [InlineData("A\n\n\n```\ncode\n```")]         // paragraph + 2 blanks + code block
    [InlineData("- a\n- b\nAfter")]               // list + adjacent paragraph
    [InlineData("- a\n- b\n\nAfter")]             // list + 1 blank + paragraph
    [InlineData("- a\n- b\n\n\nAfter")]           // list + 2 blanks + paragraph
    [InlineData("# H\n\nP")]                      // header + 1 blank + paragraph
    [InlineData("```\ncode\n```\n\nAfter")]       // code block + 1 blank + paragraph
    [InlineData("A\n\n")]                         // paragraph + trailing blank
    [InlineData("A\n\n\n")]                       // paragraph + 2 trailing blanks
    [InlineData("- a\n- b\n")]                    // list + trailing newline
    [InlineData("- a\n- b\n\n")]                  // list + trailing blank
    [InlineData("Para1\n\n# H\n\nPara2")]         // mixed pattern
    public void RoundTripPreservesBlankLines(string text)
    {
        var parsed = MarkupParser.ParseRaw(text, true).Simplify();
        var formatted1 = parsed.Format().Replace("\r\n", "\n");
        var formatted2 = MarkupFormatter.Default.Format(parsed).Replace("\r\n", "\n");
        formatted1.Should().Be(text, "Markup.Format() must round-trip blank lines");
        formatted2.Should().Be(text, "MarkupFormatter.Default must round-trip blank lines");
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

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("mailto:user@example.com")]
    [InlineData("test.user+tag@sub.domain.org")]
    public void EmailRegexValidInputTest(string input)
    {
        var p = Parse<ParagraphMarkup>(input, out var text);
        var m = p.Content.Should().BeOfType<UrlMarkup>().Subject;
        m.Url.Should().Be(text);
        m.Kind.Should().Be(UrlMarkupKind.Email);
    }

    [Theory]
    [InlineData("user@" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + ".")]
    [InlineData("user@" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "-")]
    [InlineData("user@" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "_")]
    public void EmailRegexNoBacktrackingTest(string input)
    {
        // UrlHostRe = [0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])* has a nested quantifier.
        // When the host part is a long run of alphanumeric chars followed by a char
        // in [-.\w] but NOT in [0-9a-zA-Z] (i.e. '.', '-', '_'), the regex engine
        // tries exponentially many partitions before failing.
        // The fix uses an atomic group (?>...) to prevent this backtracking.
        // These inputs go through MarkupParser -> Pidgin Email parser -> EmailRegex.IsMatch().
        var task = Task.Run(() => new MarkupParser().Parse(input));
        var completed = task.Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("parsing must complete within 5s (no regex backtracking)");
    }

    [Theory]
    [InlineData("https://" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + ".")]
    [InlineData("https://" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + "-")]
    [InlineData("www." + "aaaaaaaaaaaaaaaaaaaaaaaaaaaa" + ".")]
    public void UrlRegexNoBacktrackingTest(string input)
    {
        // Same nested quantifier issue in UrlHostRe, triggered via URL parsing path.
        var task = Task.Run(() => new MarkupParser().Parse(input));
        var completed = task.Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("parsing must complete within 5s (no regex backtracking)");
    }

    // Block quotes

    [Fact]
    public void BlockQuoteSingleLineTest()
    {
        var bq = Parse<BlockQuoteMarkup>("> hello");
        bq.Content.Should().BeAssignableTo<TextMarkup>().Which.Text.Should().Be("hello");
    }

    [Fact]
    public void BlockQuoteMultiLineTest()
    {
        var bq = Parse<BlockQuoteMarkup>("> a\n> b");
        var seq = bq.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.Count(x => x is NewLineMarkup).Should().Be(1);
        var texts = seq.Items.OfType<TextMarkup>().Select(t => t.Text).ToList();
        texts.Should().Contain("a");
        texts.Should().Contain("b");
    }

    [Fact]
    public void BlockQuoteWithMentionTest()
    {
        var bq = Parse<BlockQuoteMarkup>("> hi @u:userId");
        var seq = bq.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.OfType<UserMention>().Should().NotBeEmpty();
    }

    [Fact]
    public void BlockQuoteWithBoldTest()
    {
        var bq = Parse<BlockQuoteMarkup>("> **bold**");
        bq.Content.Should().BeOfType<StylizedMarkup>().Which.Style.Should().Be(TextStyle.Bold);
    }

    [Fact]
    public void BlockQuoteMultiLineWithMentionAndStyleTest()
    {
        // Mentions + styles across multiple quoted lines stay inside one block quote.
        var bq = Parse<BlockQuoteMarkup>("> hey @u:userId\n> see **this**");
        var seq = bq.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items.OfType<UserMention>().Should().NotBeEmpty();
        seq.Items.OfType<StylizedMarkup>().Any(s => s.Style == TextStyle.Bold).Should().BeTrue();
        seq.Items.Count(x => x is NewLineMarkup).Should().Be(1);
    }

    [Fact]
    public void BlockQuoteThenParagraphTest()
    {
        var seq = Parse<MarkupSeq>("> quote\ntext", validateFormat: false);
        seq.Items[0].Should().BeOfType<BlockQuoteMarkup>();
        seq.Items[1].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void ParagraphThenBlockQuoteTest()
    {
        var seq = Parse<MarkupSeq>("text\n> quote", validateFormat: false);
        seq.Items[0].Should().BeOfType<ParagraphMarkup>();
        seq.Items[1].Should().BeOfType<BlockQuoteMarkup>();
    }

    [Fact]
    public void GreaterWithoutSpaceIsNotBlockQuoteTest()
    {
        // ">foo" (no space after '>') is plain text, not a block quote.
        var p = Parse<ParagraphMarkup>(">foo");
        p.Content.Should().BeAssignableTo<TextMarkup>();
    }

    [Fact]
    public void BlockQuoteCannotContainCodeBlockTest()
    {
        // A '```' inside a quoted line is not a code block — the inner content is inline-only.
        var bq = Parse<BlockQuoteMarkup>("> ```not code```", validateFormat: false);
        bq.Content.Should().NotBeOfType<CodeBlockMarkup>();
    }

    [Fact]
    public void BlockQuoteFormatRoundTripsTest()
    {
        var bq = new BlockQuoteMarkup(new MarkupSeq(
            new PlainTextMarkup("a"),
            NewLineMarkup.Instance,
            new PlainTextMarkup("b")));
        bq.Format().Should().Be("> a\n> b");
    }

    // Regression: stray/ambiguous markup must never truncate the document

    [Theory]
    [InlineData("- **`a`/`b`** c", "c")]
    [InlineData("- prefixes **`a`/`u`/`c`/`p`/`e`** (author)", "author")]
    [InlineData("- one\n- **`a`/`b`** two\n- three", "three")]
    public void AmbiguousMarkupInListItemDoesNotTruncate(string input, string tail)
    {
        // A '**`a`/`b`**' that the bold grammar can't resolve must degrade to literal text,
        // not stall list-item parsing and silently drop the rest of the message.
        var text = MarkupFormatter.Default.Format(MarkupParser.ParseRaw(input).Simplify());
        text.Should().Contain(tail);
    }

    [Theory]
    [InlineData("**`a`/`b`**")]
    [InlineData("**`a`/`u`/`c`/`p`/`e`**")]
    public void AdjacentInlineElementsInsideBoldParse(string input)
    {
        // Code spans separated by a non-whitespace char must stay inside one bold span — the inline
        // combinator joins adjacent elements, not only whitespace-separated ones.
        var m = MarkupParser.ParseRaw(input).Simplify();
        m.Should().BeOfType<ParagraphMarkup>().Which.Content
            .Should().BeOfType<StylizedMarkup>().Which.Style.Should().Be(TextStyle.Bold);
        MarkupFormatter.Default.Format(m).Should().Be(input);
    }

    [Fact]
    public void MultiBlockDocumentWithAmbiguousBoldSurvives()
    {
        var input =
            "## Header\n" +
            "- `MentionId` -> **`MentionRef`**; `x` -> **`y`**.\n" +
            "- prefixes **`a`/`u`/`c`/`p`/`e`** (legacy)\n" +
            "- tail line that must survive";
        var text = MarkupFormatter.Default.Format(MarkupParser.ParseRaw(input).Simplify());
        text.Should().Contain("tail line that must survive");
    }

    [Fact]
    public void MultiMessageCopyMarkupReconstructs()
    {
        // Mirrors SelectionUI multi-message copy: each author-run is headed by an author mention on
        // its own line, runs separated by a blank line. Pasting it back runs through
        // MarkupParser.Parse (editor reconstruction) — it must keep every author mention and never
        // truncate the tail.
        var input =
            "@`Bright Virnala`a:the-actual-one:43:\n" +
            "Hello\n" +
            "World\n" +
            "\n" +
            "@`Enchanted Porthos`a:the-actual-one:45:\n" +
            "hey\n" +
            "test";
        var m = MarkupParser.ParseRaw(input).Simplify();

        var mentions = MentionExtractor.Instance.GetMentionIds(m).Select(x => x.Value).ToList();
        mentions.Should().Contain("a:the-actual-one:43");
        mentions.Should().Contain("a:the-actual-one:45");
        MarkupFormatter.Default.Format(m).Should().Contain("test");
    }

    [Theory]
    [InlineData("@`Alex`u:abcdef1234: hi", "u:abcdef1234", ": hi")]
    [InlineData("@u:abcdef1234: hi", "u:abcdef1234", ": hi")]
    [InlineData("@`Alex`u:abcdef1234. Next", "u:abcdef1234", ". Next")]
    public void MentionFollowedByPunctuationKeepsId(string input, string id, string tail)
    {
        // A ':' or '.' right after a mention id is punctuation, not part of the id — the id parser
        // must stop before it, so "@mention: text" parses the mention and renders the ": text".
        var p = MarkupParser.ParseRaw(input).Simplify().Should().BeOfType<ParagraphMarkup>().Subject;
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        var mention = seq.Items[0].Should().BeAssignableTo<MentionMarkup>().Subject;
        mention.Id.Value.Should().Be(id);
        MarkupFormatter.Default.Format(seq.Items[1]).Should().Be(tail);
    }

    [Fact]
    public void HashtagTest()
    {
        var p = Parse<ParagraphMarkup>("#promo", out var text);
        p.Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be(text);

        p = Parse<ParagraphMarkup>("#promo-2_x", out text);
        var m = p.Content.Should().BeOfType<HashtagMarkup>().Subject;
        m.Text.Should().Be(text);
        m.Tag.Should().Be("promo-2_x");

        p = Parse<ParagraphMarkup>("#тег", out text);
        p.Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be(text);

        p = Parse<ParagraphMarkup>("see #promo now", out _);
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[1].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#promo");
    }

    [Fact]
    public void HashtagFollowedByPunctuationTest()
    {
        var p = Parse<ParagraphMarkup>("#promo, right", out _);
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#promo");
        seq.Items[1].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(", right");
    }

    [Fact]
    public void HashtagInStylizedTextTest()
    {
        var p = Parse<ParagraphMarkup>("**#promo**", out _);
        var m = p.Content.Should().BeOfType<StylizedMarkup>().Subject;
        m.Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#promo");
    }

    [Fact]
    public void AdjacentHashtagsArePlainTextTest()
    {
        // Tags must be whitespace-separated; a '#' run with no separator is not a tag at all
        var p = Parse<ParagraphMarkup>("#a#b", out var text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void WhitespaceSeparatedHashtagsTest()
    {
        var p = Parse<ParagraphMarkup>("#a #b", out _);
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#a");
        seq.Items[1].Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(" ");
        seq.Items[2].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#b");
    }

    [Fact]
    public void MidWordHashIsNotAHashtagTest()
    {
        var p = Parse<ParagraphMarkup>("c#5", out var text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        p = Parse<ParagraphMarkup>("item#2", out text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void AllDigitTokenIsNotAHashtagTest()
    {
        var p = Parse<ParagraphMarkup>("#4121", out var text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void LoneOrTrailingHashIsPlainTextTest()
    {
        var p = Parse<ParagraphMarkup>("#", out var text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        p = Parse<ParagraphMarkup>("#-x", out text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        p = Parse<ParagraphMarkup>("#promo#", out text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void TooLongHashtagIsPlainTextTest()
    {
        var tooLong = "#" + new string('a', 65);
        var p = Parse<ParagraphMarkup>(tooLong, out var text);
        p.Content.Should().BeOfType<PlainTextMarkup>().Which.Text.Should().Be(text);

        var maxLength = "#" + new string('a', 64);
        p = Parse<ParagraphMarkup>(maxLength, out text);
        p.Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void HashtagDoesNotBreakHeadersTest()
    {
        var h = Parse<HeaderMarkup>("# Title");
        h.Level.Should().Be(1);
        MarkupFormatter.Default.Format(h.Content).Should().Be("Title");

        h = Parse<HeaderMarkup>("# Title with #tag", validateFormat: false);
        var seq = h.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[^1].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#tag");
    }

    [Fact]
    public void HashtagInPreformattedTextStaysTextTest()
    {
        var p = Parse<ParagraphMarkup>("`#promo`", out _);
        p.Content.Should().BeOfType<PreformattedTextMarkup>().Which.Text.Should().Be("#promo");
    }

    [Fact]
    public void HashtagAtLineStartIsNotAHeaderTest()
    {
        var p = Parse<ParagraphMarkup>("#promo\nmore", false);
        var seq = p.Content.Should().BeOfType<MarkupSeq>().Subject;
        seq.Items[0].Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#promo");
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\n\nb")]
    [InlineData("a\n\n\n\nb")]
    [InlineData("# H\n\nbody")]
    [InlineData("- x\n- y")]
    [InlineData("> q\n> r")]
    [InlineData("```\nx\n\ny\n```")]
    [InlineData("a\n\n\n\n```\nx\n\n\ny\n```\n\nb")]
    [InlineData("| a | b |\n| --- | :-: |\n| 1 | 2 |")]
    [InlineData("intro\n| a |\n| --- |\n| 1 |\n\nouttro")]
    public void NewLineStyleDoesNotAffectParseResult(string lfText)
    {
        // act
        var fromLf = MarkupFormatter.Default.Format(new MarkupParser().Parse(lfText));
        var fromCrLf = MarkupFormatter.Default.Format(new MarkupParser().Parse(lfText.Replace("\n", "\r\n")));
        var fromCr = MarkupFormatter.Default.Format(new MarkupParser().Parse(lfText.Replace("\n", "\r")));

        // assert
        fromCrLf.Should().Be(fromLf);
        fromCr.Should().Be(fromLf);
    }

    // Tables

    [Fact]
    public void TableSimpleTest()
    {
        var t = Parse<TableMarkup>("| Name | Age |\n| --- | --- |\n| Bob | 5 |");
        t.ColumnCount.Should().Be(2);
        CellText(t.Header, 0).Should().Be("Name");
        CellText(t.Header, 1).Should().Be("Age");
        t.Rows.Length.Should().Be(1);
        CellText(t.Rows[0], 0).Should().Be("Bob");
        CellText(t.Rows[0], 1).Should().Be("5");
        t.Alignments.Should().AllBeEquivalentTo(TableColumnAlignment.None);
    }

    [Fact]
    public void TableWithoutBodyRowsTest()
    {
        var t = Parse<TableMarkup>("| a | b |\n| --- | --- |");
        t.ColumnCount.Should().Be(2);
        t.Rows.Should().BeEmpty();
    }

    [Fact]
    public void TableSingleColumnTest()
    {
        var t = Parse<TableMarkup>("| a |\n| --- |\n| 1 |");
        t.ColumnCount.Should().Be(1);
        t.Rows.Length.Should().Be(1);
        CellText(t.Rows[0], 0).Should().Be("1");
    }

    [Fact]
    public void TableAlignmentsTest()
    {
        var t = Parse<TableMarkup>("| a | b | c | d |\n| --- | :-- | :-: | --: |\n| 1 | 2 | 3 | 4 |");
        t.Alignments.Should().Equal(
            TableColumnAlignment.None,
            TableColumnAlignment.Left,
            TableColumnAlignment.Center,
            TableColumnAlignment.Right);
    }

    [Theory]
    [InlineData("| a | b |\n| :--- | ---: |", TableColumnAlignment.Left, TableColumnAlignment.Right)]
    [InlineData("| a | b |\n| :-------: | - |", TableColumnAlignment.Center, TableColumnAlignment.None)]
    public void TableDelimiterCellLengthDoesNotMatterTest(
        string text,
        TableColumnAlignment first,
        TableColumnAlignment second)
    {
        var t = Parse<TableMarkup>(text, validateFormat: false);
        t.Alignments.Should().Equal(first, second);
    }

    [Fact]
    public void TableCellWhitespaceIsTrimmedTest()
    {
        var t = Parse<TableMarkup>("|   a   |b|\n|---|---|", validateFormat: false);
        CellText(t.Header, 0).Should().Be("a");
        CellText(t.Header, 1).Should().Be("b");
        t.Format().Replace("\r\n", "\n").Should().Be("| a | b |\n| --- | --- |");
    }

    [Fact]
    public void TableWithoutTrailingPipeTest()
    {
        var t = Parse<TableMarkup>("| a | b\n| --- | ---\n| 1 | 2", validateFormat: false);
        t.ColumnCount.Should().Be(2);
        CellText(t.Header, 1).Should().Be("b");
        CellText(t.Rows[0], 1).Should().Be("2");
    }

    [Fact]
    public void TableShortRowIsPaddedTest()
    {
        var t = Parse<TableMarkup>("| a | b |\n| --- | --- |\n| 1 |", validateFormat: false);
        t.Rows[0].Cells.Length.Should().Be(2);
        CellText(t.Rows[0], 0).Should().Be("1");
        CellText(t.Rows[0], 1).Should().Be("");
    }

    [Fact]
    public void TableLongRowIsTruncatedTest()
    {
        var t = Parse<TableMarkup>("| a | b |\n| --- | --- |\n| 1 | 2 | 3 |", validateFormat: false);
        t.Rows[0].Cells.Length.Should().Be(2);
        CellText(t.Rows[0], 1).Should().Be("2");
    }

    [Fact]
    public void TableEmptyCellsTest()
    {
        var t = Parse<TableMarkup>("|  |  |\n| --- | --- |");
        t.ColumnCount.Should().Be(2);
        CellText(t.Header, 0).Should().Be("");
        CellText(t.Header, 1).Should().Be("");
    }

    [Fact]
    public void TableRequiresDelimiterRowTest()
    {
        // Without a delimiter row the pipes are just text.
        var p = Parse<ParagraphMarkup>("| a | b |\n| 1 | 2 |", validateFormat: false);
        p.Content.IsBlockMarkup().Should().BeFalse();
    }

    [Fact]
    public void TableDelimiterMustMatchColumnCountTest()
    {
        var p = Parse<ParagraphMarkup>("| a | b |\n| --- |", validateFormat: false);
        p.Content.IsBlockMarkup().Should().BeFalse();
    }

    [Theory]
    [InlineData("| a |\n| x |")]
    [InlineData("| a |\n|  |")]
    [InlineData("| a |\n| : |")]
    [InlineData("| a |\n| -x- |")]
    public void TableDelimiterMustBeDashesTest(string text)
    {
        var m = MarkupParser.ParseRaw(text, true).Simplify();
        m.Should().NotBeOfType<TableMarkup>();
    }

    [Fact]
    public void SpoilerLineIsNotATableTest()
    {
        var p = Parse<ParagraphMarkup>("||secret||");
        p.Content.Should().BeOfType<StylizedMarkup>().Which.Style.Should().Be(TextStyle.Spoiler);
    }

    [Fact]
    public void TableCellsKeepInlineMarkupTest()
    {
        var t = Parse<TableMarkup>("| **b** | @u:userId |\n| --- | --- |\n| `code` | #tag |");
        t.Header.Cells[0].Content.Should().BeOfType<StylizedMarkup>().Which.Style.Should().Be(TextStyle.Bold);
        t.Header.Cells[1].Content.Should().BeOfType<UserMention>();
        t.Rows[0].Cells[0].Content.Should().BeOfType<PreformattedTextMarkup>().Which.Text.Should().Be("code");
        t.Rows[0].Cells[1].Content.Should().BeOfType<HashtagMarkup>().Which.Text.Should().Be("#tag");
    }

    [Fact]
    public void TableCellCannotContainBlockMarkupTest()
    {
        // A cell is inline-only: a '#' header or a ``` fence inside it stays text.
        var t = Parse<TableMarkup>("| # h | ``` |\n| --- | --- |", validateFormat: false);
        t.Header.Cells.Should().AllSatisfy(c => c.Content.IsBlockMarkup().Should().BeFalse());
    }

    [Fact]
    public void TableEscapedPipeInCellTest()
    {
        var t = Parse<TableMarkup>("| a \\| b | c |\n| --- | --- |");
        t.ColumnCount.Should().Be(2);
        CellText(t.Header, 0).Should().Be("a | b");
        CellText(t.Header, 1).Should().Be("c");
    }

    [Fact]
    public void TableEndsAtBlankLineTest()
    {
        var seq = Parse<MarkupSeq>("| a |\n| --- |\n| 1 |\n\ntext");
        seq.Items[0].Should().BeOfType<TableMarkup>().Which.Rows.Length.Should().Be(1);
        seq.Items[^1].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void TableEndsAtNonRowLineTest()
    {
        var seq = Parse<MarkupSeq>("| a |\n| --- |\ntext");
        seq.Items.Length.Should().Be(2);
        seq.Items[0].Should().BeOfType<TableMarkup>();
        seq.Items[1].Should().BeOfType<ParagraphMarkup>();
    }

    [Fact]
    public void ParagraphBeforeTableTest()
    {
        var seq = Parse<MarkupSeq>("text\n| a |\n| --- |");
        seq.Items.Length.Should().Be(2);
        seq.Items[0].Should().BeOfType<ParagraphMarkup>();
        seq.Items[1].Should().BeOfType<TableMarkup>();
    }

    [Fact]
    public void HeaderAndTableTest()
    {
        var seq = Parse<MarkupSeq>("# Title\n| a |\n| --- |\n## Next");
        seq.Items[0].Should().BeOfType<HeaderMarkup>();
        seq.Items[1].Should().BeOfType<TableMarkup>();
        seq.Items[2].Should().BeOfType<HeaderMarkup>();
    }

    [Fact]
    public void TwoTablesSeparatedByBlankLineTest()
    {
        var seq = Parse<MarkupSeq>("| a |\n| --- |\n\n| b |\n| --- |");
        seq.Items.OfType<TableMarkup>().Count().Should().Be(2);
    }

    [Fact]
    public void TableInListItemIsNotATableTest()
    {
        var list = Parse<ListMarkup>("- | a | b |\n- | --- | --- |", validateFormat: false);
        list.Items.Length.Should().Be(2);
        list.Items.Should().AllSatisfy(i => i.Content.IsBlockMarkup().Should().BeFalse());
    }

    [Fact]
    public void TableInBlockQuoteIsNotATableTest()
    {
        var bq = Parse<BlockQuoteMarkup>("> | a | b |\n> | --- | --- |", validateFormat: false);
        bq.Content.IsBlockMarkup().Should().BeFalse();
    }

    [Fact]
    public void TableIsBlockMarkupTest()
    {
        var t = Parse<TableMarkup>("| a |\n| --- |");
        t.Should().BeAssignableTo<BlockMarkup>();
        t.IsBlockMarkup().Should().BeTrue();
    }

    [Fact]
    public void TableFormatRoundTripTest()
    {
        // arrange
        var table = new TableMarkup(
            NewRow("a", "b"),
            [TableColumnAlignment.Left, TableColumnAlignment.Right],
            [NewRow("1", "2")]);

        // act
        var text = table.Format().Replace("\r\n", "\n");

        // assert
        text.Should().Be("| a | b |\n| :-- | --: |\n| 1 | 2 |");
        MarkupFormatter.Default.Format(table).Replace("\r\n", "\n").Should().Be(text);
    }

    [Fact]
    public void TableWithMismatchedRowThrowsTest()
    {
        // act & assert
        var build = () => new TableMarkup(NewRow("a"), [TableColumnAlignment.None], [NewRow("1", "2")]);
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TableCellRejectsBlockMarkupTest()
    {
        // act & assert
        var build = () => new TableCellMarkup(new HeaderMarkup(1, new PlainTextMarkup("h")));
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TableMentionsAreExtractedTest()
    {
        // arrange
        var t = Parse<TableMarkup>("| @u:userId |\n| --- |\n| @u:otherId |");

        // act
        var mentions = MentionExtractor.Instance.GetMentionIds(t);

        // assert
        mentions.Should().HaveCount(2);
    }

    [Fact]
    public void TableReadableTextSkipsDelimiterRowTest()
    {
        // arrange
        var t = Parse<TableMarkup>("| a | b |\n| --- | --- |\n| 1 | 2 |");

        // act
        var readable = MarkupFormatter.ReadableUnstyled.Format(t).Replace("\r\n", "\n");

        // assert
        readable.Should().Be("| a | b |\n| 1 | 2 |");
    }

    [Fact]
    public void TableIsTrimmedByWholeRowsTest()
    {
        // arrange
        var t = Parse<TableMarkup>("| aaaa | bbbb |\n| --- | --- |\n| cccc | dddd |\n| eeee | ffff |");

        // act
        var trimmed = MarkupTrimmer.Instance.Trim(t, 10);

        // assert
        var trimmedTable = trimmed.Should().BeOfType<TableMarkup>().Subject;
        trimmedTable.Rows.Length.Should().BeLessThan(t.Rows.Length);
        trimmedTable.Rows.Should().AllSatisfy(r => r.Cells.Length.Should().Be(trimmedTable.ColumnCount));
    }

    [Fact]
    public void ConcurrentParsesProduceTheSameMarkup()
    {
        // The parser hands Pidgin thread-static buffer pools, so a bug there would only show up
        // with several threads parsing at once - which nothing else in this suite does.
        // arrange
        var texts = new[] {
            "plain text with several words",
            "**bold** and *italic* and ||spoiler|| and `code`",
            "hi @u:userId, see https://example.com/x and #tag",
            "# Header\n- a\n- b\n\n> quote\n\n```\ncode\n```",
            "| a | b |\n| --- | :-: |\n| 1 | 2 |",
        };
        var parser = new MarkupParser();
        var expected = texts.Select(x => MarkupFormatter.Default.Format(parser.Parse(x))).ToArray();

        // act
        var results = new ConcurrentBag<(int Index, string Formatted)>();
        Parallel.For(0, 200, _ => {
            for (var i = 0; i < texts.Length; i++)
                results.Add((i, MarkupFormatter.Default.Format(parser.Parse(texts[i]))));
        });

        // assert
        results.Should().HaveCount(200 * texts.Length);
        results.Should().AllSatisfy(x => x.Formatted.Should().Be(expected[x.Index]));
    }

    // Helpers

    private static string CellText(TableRowMarkup row, int index)
        => MarkupFormatter.Default.Format(row.Cells[index].Content);
    private static TableRowMarkup NewRow(params string[] cells)
        => new (cells.Select(c => new TableCellMarkup(new PlainTextMarkup(c))).ToArray());

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
        AssertTopLevelIsBlocks(simplified);
        var result = simplified.Should().BeOfType<TResult>().Subject;
        if (validateFormat) {
            var expectedMarkupText = text.Replace("\r\n", "\n");
            var markupText1 = simplified.Format().Replace("\r\n", "\n");
            var markupText2 = MarkupFormatter.Default.Format(simplified).Replace("\r\n", "\n");
            markupText1.Should().Be(expectedMarkupText);
            markupText2.Should().Be(expectedMarkupText);
        }
        return result;
    }

    // Every top-level markup produced by the parser must be a BlockMarkup
    // (or a MarkupSeq whose items are all BlockMarkup).
    private static void AssertTopLevelIsBlocks(Markup markup)
    {
        if (markup is MarkupSeq seq) {
            for (var i = 0; i < seq.Items.Length; i++) {
                seq.Items[i].Should().BeAssignableTo<BlockMarkup>(
                    "every top-level item in MarkupSeq must be a BlockMarkup descendant; " +
                    $"item {i} is {seq.Items[i].GetType().Name}");
            }
        }
        else {
            markup.Should().BeAssignableTo<BlockMarkup>(
                $"top-level result must be a BlockMarkup descendant, but is {markup.GetType().Name}");
        }
    }
}
