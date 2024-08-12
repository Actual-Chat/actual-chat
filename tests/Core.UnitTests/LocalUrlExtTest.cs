namespace ActualChat.Core.UnitTests;

public class LocalUrlExtTest
{
    [Fact]
    public void IsChatCompatTest()
    {
        new LocalUrl("/chat/the-actual-one#100").IsChatCompat(out var chatId, out var entryLid).Should().BeTrue();
        chatId.Value.Should().Be("the-actual-one");
        entryLid.Should().Be(100);

        new LocalUrl("/chat/the-actual-one?n=101").IsChatCompat(out chatId, out entryLid).Should().BeTrue();
        chatId.Value.Should().Be("the-actual-one");
        entryLid.Should().Be(101);

        new LocalUrl("/chat/the-actual-one?n=102#103").IsChatCompat(out chatId, out entryLid).Should().BeTrue();
        chatId.Value.Should().Be("the-actual-one");
        entryLid.Should().Be(102);
    }

    [Fact]
    public void IsChatTest()
    {
        new LocalUrl("/chat/the-actual-one?n=100").IsChatCompat(out var chatId, out var entryLid).Should().BeTrue();
        chatId.Value.Should().Be("the-actual-one");
        entryLid.Should().Be(100);

        new LocalUrl("/chat/s-P7oXNDTeHL-752w3sfrad?n=101").IsChatCompat(out chatId, out entryLid).Should().BeTrue();
        chatId.Value.Should().Be("s-P7oXNDTeHL-752w3sfrad");
        entryLid.Should().Be(101);
    }

}
