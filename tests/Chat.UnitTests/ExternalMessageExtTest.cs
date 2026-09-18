using ActualChat.External;

namespace ActualChat.Chat.UnitTests;

public class ExternalMessageExtTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public async Task StreamingEntryShouldNotExposeText()
    {
        // arrange
        var authorId = AuthorId.New(TestChatId, 5);
        var entry = new TextEntry(ChatEntryId.New(TestChatId, 1), 1) {
            AuthorId = authorId,
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "partial transcript so far",
            ContentStreamId = "stream-1",
        };
        var author = new AuthorFull(UserId.New(), authorId, 1) {
            Avatar = new Avatar("avatar-1") { Name = "Alexey" },
        };
        var urlMapper = new UrlMapper("https://voxt.ai/");
        var markupParser = new MarkupParser();

        // act
        var message = await entry.ToExternalMessage(
            author, includeText: true, urlMapper,
            (_, _) => Task.FromResult<string?>(null), markupParser, CancellationToken.None);

        // assert
        message.IsStreaming.Should().BeTrue();
        message.Text.Should().BeNull("in-progress transcription is never exposed, even when includeText is true");
    }
}
