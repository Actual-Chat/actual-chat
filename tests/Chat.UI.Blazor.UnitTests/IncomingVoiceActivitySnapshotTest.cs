using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class IncomingVoiceActivitySnapshotTest
{
    [Fact]
    public void StampsOnEmptyToNonEmpty()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: true).Should().BeTrue();
    }

    [Fact]
    public void DoesNotStampWhileStillStreaming()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: true).Should().BeFalse();
    }

    [Fact]
    public void DoesNotStampOnStop()
    {
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: false).Should().BeFalse();
        IncomingVoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: false).Should().BeFalse();
    }
}
