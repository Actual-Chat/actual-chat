using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public class AttentionHoldTest
{
    private const string Chat = "/chat/testchatid1234567890";

    [Theory]
    [InlineData(Chat, Chat, true)]
    [InlineData(Chat + "?n=42", Chat, true)]
    [InlineData(Chat + "#42", Chat, true)]
    [InlineData("/chat/otherchatid123456789", Chat, false)]
    [InlineData("/chats", Chat, false)]
    [InlineData("/join/abc?x=1", "/join/abc", true)]
    [InlineData("/join/abcd", "/join/abc", false)]
    [InlineData("/settings", "/join/abc", false)]
    public void IsSameTargetShouldIgnoreEntryJumpsButNotOtherPages(string url, string target, bool expected)
        => AttentionHold.IsSameTarget(url, target).Should().Be(expected);
}
