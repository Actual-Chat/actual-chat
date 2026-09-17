using ActualChat.WebHooks;

namespace ActualChat.Chat.UnitTests;

public class WebHookPayloadsTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public async Task MessagePostedShouldCarryEnvelopeAndMessage()
    {
        // arrange
        var payloads = new WebHookPayloads(CreateServices());
        var hook = CreateHook(includeText: true);
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new TextEntry(ChatEntryId.New(TestChatId, 1), 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "hi **all** @a:" + authorId.Value,
        };
        var author = new AuthorFull(UserId.New(), authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Alexey" },
        };

        // act
        var json = await payloads.Message(
            hook, WebHookEvents.MessagePosted, entry, null, author, CancellationToken.None);

        // assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("message.posted");
        root.GetProperty("hook").GetProperty("scope").GetString().Should().Be("chat");
        var message = root.GetProperty("data").GetProperty("message");
        message.GetProperty("text").GetString().Should().Be(entry.Content);
        message.GetProperty("author").GetProperty("name").GetString().Should().Be("Alexey");
        message.GetProperty("origin").GetProperty("kind").GetString().Should().Be("user");
        json.Should().NotContain("userId");
    }

    [Fact]
    public async Task IncludeTextFalseShouldOmitText()
    {
        // arrange
        var payloads = new WebHookPayloads(CreateServices());
        var hook = CreateHook(includeText: false);
        var authorId = AuthorId.New(TestChatId, 5);
        var entryId = ChatEntryId.New(TestChatId, 1);
        var previous = new TextEntry(entryId, 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "old text",
        };
        var entry = new TextEntry(entryId, 2) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "new text",
        };
        var author = new AuthorFull(UserId.New(), authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Alexey" },
        };

        // act
        var json = await payloads.Message(
            hook, WebHookEvents.MessageEdited, entry, previous, author, CancellationToken.None);

        // assert
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("message").TryGetProperty("text", out _).Should().BeFalse();
        data.GetProperty("previous").TryGetProperty("text", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ViaApiEntryShouldReportApiOrigin()
    {
        // arrange
        var payloads = new WebHookPayloads(CreateServices());
        var hook = CreateHook(includeText: true);
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new TextEntry(ChatEntryId.New(TestChatId, 1), 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "hi",
            IsViaApi = true,
        };
        var author = new AuthorFull(UserId.New(), authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Bot user" },
        };

        // act
        var json = await payloads.Message(
            hook, WebHookEvents.MessagePosted, entry, null, author, CancellationToken.None);

        // assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data").GetProperty("message").GetProperty("origin").GetProperty("kind")
            .GetString().Should().Be("api");
    }

    [Fact]
    public async Task OversizedTextShouldBeTruncated()
    {
        // arrange
        var payloads = new WebHookPayloads(CreateServices());
        var hook = CreateHook(includeText: true);
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new TextEntry(ChatEntryId.New(TestChatId, 1), 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = new string('a', 300 * 1024),
        };
        var author = new AuthorFull(UserId.New(), authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Alexey" },
        };

        // act
        var json = await payloads.Message(
            hook, WebHookEvents.MessagePosted, entry, null, author, CancellationToken.None);

        // assert
        json.Length.Should().BeLessThan(Constants.WebHooks.MaxPayloadLength);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("data").GetProperty("message").GetProperty("textTruncated").GetBoolean()
            .Should().BeTrue();
    }

    [Fact]
    public void PingShouldHaveNoChatBlock()
    {
        // arrange
        var payloads = new WebHookPayloads(CreateServices());
        var hook = CreateHook(includeText: true);

        // act
        var json = payloads.Ping(hook, "Alexey");

        // assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("chat", out _).Should().BeFalse();
        doc.RootElement.GetProperty("type").GetString().Should().Be("ping");
        doc.RootElement.GetProperty("data").GetProperty("sentBy").GetString().Should().Be("Alexey");
    }

    // Private methods

    private static WebHook CreateHook(bool includeText)
        => new(WebHookId.New(), 1) {
            Scope = WebHookScope.Chat,
            ScopeId = TestChatId.Value,
            Kind = WebHookKind.Outgoing,
            Name = "Test hook",
            Url = "https://example.com/hook",
            Events = WebHookEvents.Messages | WebHookEvents.Ping,
            IncludeText = includeText,
        };

    private static ServiceProvider CreateServices()
    {
        var chatsBackend = new Mock<IChatsBackend>(MockBehavior.Loose);
        chatsBackend
            .Setup(x => x.Get(It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Chat?)null);
        var mediaBackend = new Mock<IMediaBackend>(MockBehavior.Loose);
        mediaBackend
            .Setup(x => x.Get(It.IsAny<MediaId?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Media.Media?)null);

        return new ServiceCollection()
            .AddSingleton(chatsBackend.Object)
            .AddSingleton(mediaBackend.Object)
            .AddSingleton<IMarkupParser>(new MarkupParser())
            .AddSingleton(new UrlMapper("https://voxt.ai/"))
            .AddSingleton(MomentClockSet.Default)
            .BuildServiceProvider();
    }
}
