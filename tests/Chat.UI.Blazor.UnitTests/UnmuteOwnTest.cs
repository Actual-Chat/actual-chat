using ActualChat.Live;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class UnmuteOwnTest
{
    [Fact]
    public void MustUnmuteOwnWhenMicIsMuted()
    {
        // arrange
        var me = new LiveSessionMember { MicMuted = true };

        // act
        var result = LiveSessionUI.MustUnmuteOwn(me);

        // assert
        result.Should().BeTrue();
    }

    [Fact]
    public void MustNotUnmuteOwnWhenMicIsNotMuted()
    {
        // arrange
        var me = new LiveSessionMember { MicMuted = false };

        // act
        var result = LiveSessionUI.MustUnmuteOwn(me);

        // assert
        result.Should().BeFalse();
    }

    [Fact]
    public void MustUnmuteOwnWhenMemberIsNotLoadedYet()
    {
        // act
        var result = LiveSessionUI.MustUnmuteOwn(null);

        // assert
        result.Should().BeTrue("a cold replica reads as null, and the user may be muted on the server");
    }
}
