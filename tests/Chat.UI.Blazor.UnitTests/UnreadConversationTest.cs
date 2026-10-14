using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class UnreadConversationTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Reader = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Other = AuthorId.New(TestChatId, 2);

    [Fact]
    public void ConversationPastReadPositionShouldBeUnread()
    {
        // arrange
        var conversation = NewConversation(100, 200, [Other]);

        // act
        var isUnread = ChatUI.IsUnreadForReader(conversation, 150, Reader);

        // assert
        isUnread.Should().BeTrue();
    }

    [Fact]
    public void ConversationBeforeReadPositionShouldNotBeUnread()
    {
        // arrange
        var conversation = NewConversation(100, 200, [Other]);

        // act
        var isUnread = ChatUI.IsUnreadForReader(conversation, 200, Reader);

        // assert
        isUnread.Should().BeFalse();
    }

    [Fact]
    public void ConversationOfOnlyReaderShouldNotBeUnread()
    {
        // arrange
        var conversation = NewConversation(100, 200, [Reader]);

        // act
        var isUnread = ChatUI.IsUnreadForReader(conversation, 0, Reader);

        // assert
        isUnread.Should().BeFalse("own messages are never new to the sender");
    }

    [Fact]
    public void ConversationOfReaderAndOthersShouldBeUnread()
    {
        // arrange
        var conversation = NewConversation(100, 200, [Reader, Other]);

        // act
        var isUnread = ChatUI.IsUnreadForReader(conversation, 0, Reader);

        // assert
        isUnread.Should().BeTrue();
    }

    private static Conversation NewConversation(long start, long end, AuthorId[] authorIds)
        => new(ConversationId.New(TestChatId, start)) {
            EndEntryLid = end,
            AuthorIds = authorIds,
        };
}
