using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatListHistoryAppendTest
{
    [Fact]
    public void ActiveRowsAndHistoryShouldBeSplitByOneDivider()
    {
        // arrange
        var items = NewChatItems(3);

        // act
        var (separatorIndexes, totalCount) = ChatList.AppendHistory(items, 3, NewHistory(2), [0]);

        // assert
        items[2].IsLastItemInBlock.Should().BeTrue("the last active row renders the divider");
        items.Skip(3).Select(x => x.Position).Should().Equal([3, 4]);
        items.Skip(3).Should().OnlyContain(x => x.History != null);
        separatorIndexes.Should().Equal([0, 2]);
        totalCount.Should().Be(5);
    }

    [Fact]
    public void HistoryOnlyShouldStartAtZeroWithoutADivider()
    {
        // arrange
        var items = new List<ChatListItemModel>();

        // act
        var (separatorIndexes, totalCount) = ChatList.AppendHistory(items, 0, NewHistory(2), []);

        // assert
        items.Select(x => x.Position).Should().Equal([0, 1]);
        items.Should().OnlyContain(x => !x.IsLastItemInBlock);
        separatorIndexes.Should().BeEmpty("there is no active block to divide from");
        totalCount.Should().Be(2);
    }

    [Fact]
    public void ActiveRowsWithoutHistoryShouldBeUnchanged()
    {
        // arrange
        var items = NewChatItems(3);
        IReadOnlyList<int> given = [1];

        // act
        var (separatorIndexes, totalCount) = ChatList.AppendHistory(items, 3, [], given);

        // assert
        items.Should().HaveCount(3);
        items.Should().OnlyContain(x => !x.IsLastItemInBlock);
        separatorIndexes.Should().BeSameAs(given);
        totalCount.Should().Be(3);
    }

    [Fact]
    public void EmptyListWithoutHistoryShouldStayEmpty()
    {
        // arrange
        var items = new List<ChatListItemModel>();

        // act
        var (separatorIndexes, totalCount) = ChatList.AppendHistory(items, 0, [], []);

        // assert
        items.Should().BeEmpty();
        separatorIndexes.Should().BeEmpty();
        totalCount.Should().Be(0);
    }

    [Fact]
    public void HistoryShouldStartBelowATileThatOverranTheCount()
    {
        // arrange - GetCount and the tiles are separate reads, so the window can hold a row more
        var items = NewChatItems(4);

        // act
        var (separatorIndexes, totalCount) = ChatList.AppendHistory(items, 3, NewHistory(2), [3]);

        // assert
        items.Skip(4).Select(x => x.Position).Should().Equal([4, 5], "nothing may share a position");
        separatorIndexes.Should().Equal([3], "the divider follows the last row, and is never doubled");
        totalCount.Should().Be(6);
    }

    [Fact]
    public void PartialWindowShouldNotGetTheHistoryBlock()
    {
        // arrange - GetData appends only where the window reaches the last chat
        const int chatCount = 10;
        const int windowEnd = 3;
        var items = NewChatItems(windowEnd);
        var history = NewHistory(2);
        IReadOnlyList<int> given = [];
        var hasAllChats = windowEnd >= chatCount;

        // act
        var (separatorIndexes, totalCount) = hasAllChats
            ? ChatList.AppendHistory(items, chatCount, history, given)
            : (given, chatCount + history.Count);

        // assert
        items.Should().HaveCount(windowEnd);
        items.Should().NotContain(x => x.History != null, "the history block closes the whole list");
        separatorIndexes.Should().BeEmpty();
        totalCount.Should().Be(12, "the rows below the window still count");
    }

    // Private methods

    private static List<ChatListItemModel> NewChatItems(int count)
        => Enumerable
            .Range(0, count)
            .Select(i => new ChatListItemModel(i, new Chat(ChatId.Parse(GroupChatId.New().Value)), false, i == 0))
            .ToList();

    private static ApiArray<NotificationHistoryGroup> NewHistory(int count)
        => Enumerable
            .Range(0, count)
            .Select(i => {
                var chatId = ChatId.Parse(GroupChatId.New().Value);
                var newest = new NotificationHistoryItem(i + 1, NotificationKind.Mention) { ChatId = chatId };
                return new NotificationHistoryGroup(chatId, newest, 0);
            })
            .ToApiArray();
}
