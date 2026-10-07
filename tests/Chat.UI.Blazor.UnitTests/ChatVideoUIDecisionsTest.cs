using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatVideoUIDecisionsTest
{
    [Theory]
    [InlineData(VisualActivityPanelMode.Inline, false, VisualActivityPanelMode.Collapsed)]
    [InlineData(VisualActivityPanelMode.Inline, true, VisualActivityPanelMode.Inline)]
    [InlineData(VisualActivityPanelMode.Collapsed, false, VisualActivityPanelMode.Collapsed)]
    [InlineData(VisualActivityPanelMode.Collapsed, true, VisualActivityPanelMode.Collapsed)]
    [InlineData(VisualActivityPanelMode.Hidden, false, VisualActivityPanelMode.Hidden)]
    [InlineData(VisualActivityPanelMode.Expanded, false, VisualActivityPanelMode.Expanded)]
    public void CallVideoShouldFloatOffItsChatOnly(
        VisualActivityPanelMode mode, bool isOnChatPage, VisualActivityPanelMode expectedMode)
    {
        // act
        var shownMode = ChatVideoUI.DecideCallVideoPanelMode(mode, isOnChatPage);

        // assert
        shownMode.Should().Be(expectedMode);
    }
}
