namespace ActualChat.Chat.UnitTests;

public class InboundPayloadTest
{
    [Fact]
    public void JsonShouldParseTextReplyAndCards()
    {
        // arrange
        const string body = """
            {"text":"hi","replyTo":42,"attachments":[{"title":"Build","title_link":"https://ci/b/1","text":"ok",
             "fields":[{"title":"Branch","value":"main","short":true}],"image_url":"https://ci/a.png","color":"#f00",
             "username":"spoof","unknown":1}]}
            """;

        // act
        var payload = InboundPayloadParser.Parse("application/json", body)!;

        // assert
        payload.Text.Should().Be("hi");
        payload.ReplyTo.Should().Be(42);
        payload.Attachments.Should().ContainSingle();
        var card = payload.Attachments[0];
        card.Title.Should().Be("Build");
        card.TitleLink.Should().Be("https://ci/b/1");
        card.Fields.Should().ContainSingle(f => f.Title == "Branch" && f.Value == "main");
        card.ImageUrl.Should().Be("https://ci/a.png");
    }

    [Fact]
    public void FormPayloadShouldParseLikeJson()
    {
        // arrange
        var body = "payload=" + Uri.EscapeDataString("""{"text":"from form"}""");

        // act
        var payload = InboundPayloadParser.Parse("application/x-www-form-urlencoded", body)!;

        // assert
        payload.Text.Should().Be("from form");
    }

    [Fact]
    public void JsonWithCharsetShouldParse()
    {
        // arrange
        const string body = """{"text":"charset test"}""";

        // act
        var payload = InboundPayloadParser.Parse("application/json; charset=utf-8", body)!;

        // assert
        payload.Text.Should().Be("charset test");
    }

    [Theory]
    [InlineData("application/json", "not json")]
    [InlineData("application/json", "[1,2]")]
    [InlineData("application/x-www-form-urlencoded", "other=1")]
    [InlineData("text/plain", "hello")]
    public void GarbageShouldParseToNull(string contentType, string body)
        => InboundPayloadParser.Parse(contentType, body).Should().BeNull();

    [Fact]
    public void FoldShouldComposeMarkupInDocumentedOrder()
    {
        // arrange
        var payload = new InboundPayload("Deploy done", null, [
            new InboundCard("Build #1", "https://ci/b/1", "CI result", "All green",
                [new InboundField("Branch", "main"), new InboundField("Tests", "812")],
                "https://ci/a.png", "https://ci/t.png", "ci.example.com"),
        ]);

        // act
        var (markup, images) = SlackCardFolder.Fold(payload);

        // assert
        markup.Should().Be(
            "Deploy done\n\n" +
            "CI result\n" +
            "**Build #1** https://ci/b/1\n" +
            "All green\n" +
            "Branch: main\n" +
            "Tests: 812\n" +
            "ci.example.com");
        images.Should().Equal("https://ci/a.png", "https://ci/t.png");
    }

    [Fact]
    public void FoldShouldSkipEmptyPartsAndCapImages()
    {
        // arrange
        var cards = Enumerable.Range(0, 6)
            .Select(i => new InboundCard(null, null, null, null, [], $"https://ci/{i}.png", null, null))
            .ToArray();
        var payload = new InboundPayload(null, null, cards);

        // act
        var (markup, images) = SlackCardFolder.Fold(payload);

        // assert
        markup.Should().BeEmpty();
        images.Should().HaveCount(Constants.WebHooks.InboundImageLimit);
    }

    [Fact]
    public void FoldShouldIgnoreNonHttpsImages()
    {
        // arrange
        var payload = new InboundPayload("x", null, [
            new InboundCard(null, null, null, null, [], "ftp://ci/a.png", "javascript:alert(1)", null),
        ]);

        // act
        var (_, images) = SlackCardFolder.Fold(payload);

        // assert
        images.Should().BeEmpty();
    }
}
