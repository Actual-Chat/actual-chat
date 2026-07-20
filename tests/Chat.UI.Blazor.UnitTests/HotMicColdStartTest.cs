using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HotMicColdStartTest
{
    private static readonly TimeSpan Cold = TimeSpan.FromSeconds(15);

    [Fact]
    public void ClosesWhenNeverVoicedPastTimeout()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold + TimeSpan.FromSeconds(1), Cold)
            .Should().BeTrue();
    }

    [Fact]
    public void StaysOpenBeforeTimeout()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold - TimeSpan.FromSeconds(1), Cold)
            .Should().BeFalse();
    }

    [Fact]
    public void NeverColdClosesOnceVoiced()
    {
        WalkieTalkieReplyUI.ShouldColdClose(everVoiced: true, elapsed: Cold + TimeSpan.FromMinutes(5), Cold)
            .Should().BeFalse();
    }
}
