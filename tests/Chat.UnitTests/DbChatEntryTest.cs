using ActualChat.Chat.Db;
using ActualChat.Media;

namespace ActualChat.Chat.UnitTests;

public sealed class DbChatEntryTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Theory]
    [InlineData(LinkPreviewMode.None)]
    [InlineData(LinkPreviewMode.Compact)]
    [InlineData(LinkPreviewMode.Full)]
    public void LinkPreviewModeShouldSurviveDbRoundTrip(LinkPreviewMode linkPreviewMode)
    {
        // arrange
        var chatId = ChatId.Parse("the-actual-one");
        var model = new TextEntry(ChatEntryId.New(chatId, 1), 1) {
            AuthorId = AuthorId.New(chatId, 10),
            BeginsAt = new Moment(DateTime.UtcNow),
            Content = "📍 Location: https://www.openstreetmap.org/?mlat=10&mlon=20",
            LinkPreviewMode = linkPreviewMode,
        };

        // act
        var restored = new DbChatEntry(model).ToModel();

        // assert
        restored.LinkPreviewMode.Should().Be(linkPreviewMode);
    }
}
