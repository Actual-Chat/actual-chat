using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatListQueryRangeTest
{
    [Fact]
    public void ForwardMoveShouldReachTheRowAfterTheLoadedWindow()
    {
        // arrange - rows 55..114 are loaded, so the window is [55, 115) and the last key is "114"
        var keyRange = new Range<string>("55", "114");

        // act - the list wants rows 57..115, i.e. [57, 116): start +2, end +1
        var range = ChatList.GetQueryRange(keyRange, new Range<int>(2, 1));

        // assert
        range.Should().Be(new Range<int>(57, 116), "the last key is inclusive, so the window ends one past it");
    }

    [Fact]
    public void NoMoveShouldKeepTheLoadedWindow()
    {
        // arrange
        var keyRange = new Range<string>("10", "29");

        // act
        var range = ChatList.GetQueryRange(keyRange, new Range<int>(0, 0));

        // assert
        range.Should().Be(new Range<int>(10, 30));
    }

    [Fact]
    public void BackwardMoveShouldExtendTheStartOnly()
    {
        // arrange
        var keyRange = new Range<string>("40", "59");

        // act
        var range = ChatList.GetQueryRange(keyRange, new Range<int>(-10, 0));

        // assert
        range.Should().Be(new Range<int>(30, 60));
    }
}
