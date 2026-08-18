using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class HotMicColdStartTest
{
    private static readonly TimeSpan Cold = TimeSpan.FromSeconds(15);

    [Fact]
    public void ClosesWhenNeverVoicedPastTimeout()
    {
        PttReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold + TimeSpan.FromSeconds(1), Cold)
            .Should().BeTrue();
    }

    [Fact]
    public void StaysOpenBeforeTimeout()
    {
        PttReplyUI.ShouldColdClose(everVoiced: false, elapsed: Cold - TimeSpan.FromSeconds(1), Cold)
            .Should().BeFalse();
    }

    [Fact]
    public void NeverColdClosesOnceVoiced()
    {
        PttReplyUI.ShouldColdClose(everVoiced: true, elapsed: Cold + TimeSpan.FromMinutes(5), Cold)
            .Should().BeFalse();
    }
}
