namespace ActualChat.Notifications.IntegrationTests;

public class NotificationMessageTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void NewTruncatesLongText()
    {
        // arrange
        var text = new string('x', 300);

        // act
        var message = NotificationMessage.New(AuthorId.New(TestChatId, 1), "Alice", text, 100, Moment.Now);

        // assert
        message.Text.Length.Should().Be(Constants.Notification.MaxRecentMessageTextLength);
        message.Text.Should().EndWith("…");
    }

    [Fact]
    public void NewKeepsShortTextVerbatim()
    {
        // act
        var message = NotificationMessage.New(AuthorId.New(TestChatId, 1), "Alice", "hello", 100, Moment.Now);

        // assert
        message.Text.Should().Be("hello");
        message.AuthorName.Should().Be("Alice");
        message.EntryLid.Should().Be(100);
    }
}
