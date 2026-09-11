using ActualChat.Live;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class OutgoingCallBannerTest
{
    [Fact]
    public void EndedStatusShouldHaveNoDisplayText()
    {
        // Ended is reachable after every genuinely-completed call (Derive returns it once the
        // session ends, not just on some rare failure path) - IsVisible gates on this being
        // non-empty, so a blank arm here would leave a visible-but-textless banner up for as
        // long as the CallState survives.

        // arrange
        var banner = new OutgoingCallBanner();

        // act
        var text = banner.Text(CallerStatus.Ended);

        // assert
        text.Should().BeEmpty();
    }

    [Fact]
    public void CanceledStatusShouldHaveNoDisplayText()
    {
        // Canceled is documented as unreachable from any client (CancelCall clears CallState
        // immediately), but nothing enforces that invariant at this call site - guarded anyway.

        // arrange
        var banner = new OutgoingCallBanner();

        // act
        var text = banner.Text(CallerStatus.Canceled);

        // assert
        text.Should().BeEmpty();
    }
}
