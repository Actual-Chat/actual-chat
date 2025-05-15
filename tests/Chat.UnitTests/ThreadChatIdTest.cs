namespace ActualChat.Chat.UnitTests;

public class ThreadChatIdTest
{
    [Fact]
    public void ParseSimpleThreadChatId()
    {
        var chatId = ChatId.ParseNullable("the-actual-one-1000");
        chatId.Should().NotBeNull();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(1000);
        chatId.GetThreadParent().Value.Should().Be("the-actual-one");
    }

    [Fact]
    public void ParseHierarchicalThreadChatId()
    {
        var chatId = ChatId.ParseNullable("the-actual-one-1000-50");
        chatId.Should().NotBeNull();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(50);
        chatId = chatId.GetThreadParent();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(1000);
        chatId.GetThreadParent().Value.Should().Be("the-actual-one");
    }

    [Fact]
    public void PlaceThreadChatIdShouldBeParsed()
    {
        var chatId = ChatId.ParseNullable("s-t5i1xQXr0X-YeehSAvEXG-10");
        chatId.Should().NotBeNull();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(10);
        chatId.GetThreadParentOrSelf().Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG");
    }

    [Fact]
    public void PlaceThreadChatIdShouldBeParsed2()
    {
        var chatId = ChatId.ParseNullable("s-t5i1xQXr0X-YeehSAvEXG-10-3");
        chatId.Should().NotBeNull();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(3);
        chatId = chatId.GetThreadParent();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(10);
        chatId.Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG-10");
        chatId.GetThreadParent().Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG");
    }
}
