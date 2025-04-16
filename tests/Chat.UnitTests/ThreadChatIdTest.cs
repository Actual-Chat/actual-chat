namespace ActualChat.Chat.UnitTests;

public class ThreadChatIdTest
{
    // [Fact]
    // public void Test1()
    // {
    //     var chatId = ChatId.Parse("the-actual-one-1000");
    //     chatId.IsNone.Should().BeFalse();
    //     chatId.IsThread.Should().BeTrue();
    //     chatId.ThreadId.Should().Be(1000);
    //     chatId.Parent.Value.Should().Be("the-actual-one");
    // }
    //
    // [Fact]
    // public void Test2()
    // {
    //     var chatId = ChatId.Parse("the-actual-one-1000-50");
    //     chatId.IsNone.Should().BeFalse();
    //     chatId.IsThread.Should().BeTrue();
    //     chatId.ThreadId.Should().Be(50);
    //     chatId.Parent.Value.Should().Be("the-actual-one");
    // }

    [Fact]
    public void PlaceThreadChatIdShouldBeParsed()
    {
        var chatId = ChatId.Parse("s-t5i1xQXr0X-YeehSAvEXG-10");
        chatId.IsNone.Should().BeFalse();
        chatId.IsThread.Should().BeTrue();
        chatId.ThreadId.Should().Be(10);
        chatId.Parent.Value.Should().Be("s-t5i1xQXr0X-YeehSAvEXG");
    }
}
