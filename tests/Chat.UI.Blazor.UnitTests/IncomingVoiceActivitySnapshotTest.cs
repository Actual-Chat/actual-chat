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

    [Fact]
    public void StampsEndOnStop()
    {
        // The answer window runs from the END of the utterance: without the end stamp a short
        // window would expire mid-message and leave nothing to reply to.
        IncomingVoiceActivityUI.ShouldStampEnd(prevHadOthers: true, nowHasOthers: false).Should().BeTrue();
    }

    [Fact]
    public void DoesNotStampEndWithoutAFallingEdge()
    {
        IncomingVoiceActivityUI.ShouldStampEnd(prevHadOthers: false, nowHasOthers: true).Should().BeFalse();
        IncomingVoiceActivityUI.ShouldStampEnd(prevHadOthers: true, nowHasOthers: true).Should().BeFalse();
        IncomingVoiceActivityUI.ShouldStampEnd(prevHadOthers: false, nowHasOthers: false).Should().BeFalse();
    }

    [Fact]
    public void LiveIncomingVoiceOverridesItsStampWithNow()
    {
        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var chatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);
        var stamps = new Dictionary<ChatId, Moment> {
            [chatA] = now - TimeSpan.FromSeconds(100),
            [chatB] = now - TimeSpan.FromSeconds(100),
        };

        // act
        var snapshot = IncomingVoiceActivityUI.BuildSnapshot(stamps, [chatA], now);

        // assert: a chat still streaming stays inside any window; the rest keep their stamps
        snapshot[chatA].Should().Be(now, "live incoming voice must keep the answer window open");
        snapshot[chatB].Should().Be(now - TimeSpan.FromSeconds(100));
    }

    [Fact]
    public void LiveIncomingVoiceAppearsEvenWithoutAStamp()
    {
        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);

        // act
        var snapshot = IncomingVoiceActivityUI.BuildSnapshot(new Dictionary<ChatId, Moment>(), [chatA], now);

        // assert
        snapshot[chatA].Should().Be(now);
    }
}
