namespace ActualChat.Users.UnitTests;

public sealed class ChatPositionTest
{
    private static readonly ChatId GroupChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId PeerChatId = ChatId.Parse("p-Nb5srp-pKGsAk");

    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(0, 10, 10)]
    [InlineData(4, 10, 6)]
    [InlineData(10, 10, 0)]
    [InlineData(12, 10, 0)]
    public void GetUnreadCountShouldCountFromTheReadPositionInAGroupChat(long readEntryLid, long lastId, long expected)
    {
        // act
        var unreadCount = ChatPosition.GetUnreadCount(GroupChatId, readEntryLid, lastId);

        // assert
        unreadCount.Should().Be(expected);
    }

    [Fact]
    public void GetUnreadCountShouldCountAPeerChatWithoutPositionFromTheStart()
    {
        // act
        var unreadCount = ChatPosition.GetUnreadCount(PeerChatId, ChatPosition.None.EntryLid, 10);

        // assert
        unreadCount.Should().Be(10);
    }
}
