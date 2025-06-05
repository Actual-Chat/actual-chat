namespace ActualChat.Chat.UnitTests;

public class ThreadChatIdTest
{
    [Fact]
    public void ParseSimpleThreadChatId()
    {
        var chatId = ChatId.ParseNullable("the-actual-one-1000");
        chatId.Should().NotBeNull();
        chatId.IsThread(out var threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(1000);
        threadChatId.ParentChatId.Value.Should().Be("the-actual-one");
    }

    [Fact]
    public void ParseHierarchicalThreadChatId()
    {
        var chatId = ChatId.ParseNullable("the-actual-one-1000-50");
        chatId.Should().NotBeNull();
        chatId.IsThread(out var threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(50);
        chatId = threadChatId.ParentChatId;
        chatId.IsThread(out threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(1000);
        threadChatId.ParentChatId.Value.Should().Be("the-actual-one");
    }

    [Fact]
    public void PlaceThreadChatIdShouldBeParsed()
    {
        var chatId = ChatId.ParseNullable("s-t5i1xQXr0X-YeehSAvEXG-10");
        chatId.Should().NotBeNull();
        chatId.IsThread(out var threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(10);
        threadChatId.ParentChatId.Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG");
    }

    [Fact]
    public void PlaceThreadChatIdShouldBeParsed2()
    {
        var chatId = ChatId.ParseNullable("s-t5i1xQXr0X-YeehSAvEXG-10-3");
        chatId.Should().NotBeNull();
        chatId.IsThread(out var threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(3);
        chatId = threadChatId.ParentChatId;
        chatId.IsThread(out threadChatId).Should().BeTrue();
        threadChatId!.ThreadId.Should().Be(10);
        threadChatId.Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG-10");
        threadChatId.ParentChatId.Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG");
    }

    [Fact]
    public void ContactIdToGroupChatThread()
    {
        var chatId = ChatId.ParseNullable("the-actual-one-1000");
        var userId = UserId.New();
        var contactId = ContactId.NewAny(userId, chatId);
        contactId.Should().NotBeNull();
    }

    [Fact]
    public void ContactIdToPlaceChatThread()
    {
        var chatId = ChatId.ParseNullable("s-t5i1xQXr0X-YeehSAvEXG-10");
        var userId = UserId.New();
        var contactId = ContactId.NewAny(userId, chatId);
        contactId.Should().NotBeNull();
    }
}
