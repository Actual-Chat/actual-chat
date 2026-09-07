using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class VoiceActivitySnapshotTest
{
    [Fact]
    public void StampsOnEmptyToNonEmpty()
    {
        VoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: true).Should().BeTrue();
    }

    [Fact]
    public void DoesNotStampWhileStillStreaming()
    {
        VoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: true).Should().BeFalse();
    }

    [Fact]
    public void DoesNotStampOnStop()
    {
        VoiceActivityUI.ShouldStamp(prevHadOthers: true, nowHasOthers: false).Should().BeFalse();
        VoiceActivityUI.ShouldStamp(prevHadOthers: false, nowHasOthers: false).Should().BeFalse();
    }

    [Fact]
    public void StampsEndOnStop()
    {
        // The answer window runs from the END of the utterance: without the end stamp a short
        // window would expire mid-message and leave nothing to reply to.
        VoiceActivityUI.ShouldStampEnd(prevHadOthers: true, nowHasOthers: false).Should().BeTrue();
    }

    [Fact]
    public void DoesNotStampEndWithoutAFallingEdge()
    {
        VoiceActivityUI.ShouldStampEnd(prevHadOthers: false, nowHasOthers: true).Should().BeFalse();
        VoiceActivityUI.ShouldStampEnd(prevHadOthers: true, nowHasOthers: true).Should().BeFalse();
        VoiceActivityUI.ShouldStampEnd(prevHadOthers: false, nowHasOthers: false).Should().BeFalse();
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
        var snapshot = VoiceActivityUI.BuildSnapshot(stamps, [chatA], now);

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
        var snapshot = VoiceActivityUI.BuildSnapshot(new Dictionary<ChatId, Moment>(), [chatA], now);

        // assert
        snapshot[chatA].Should().Be(now);
    }

    [Fact]
    public void OwnVoiceShouldOpenTheWindowWhereNobodyElseSpoke()
    {
        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);

        // act
        var merged = VoiceActivityUI.MergeSnapshots(
            new Dictionary<ChatId, Moment>(),
            new Dictionary<ChatId, Moment> { [chatA] = now });

        // assert
        merged[chatA].Should().Be(now, "your own utterance arms the reply triggers too");
    }

    [Fact]
    public void TheLaterSideOfTheConversationShouldWin()
    {
        // The point of the merge: a 30s reply of yours would otherwise leave the window dated
        // from the incoming utterance it answered, and your follow-up couldn't be gestured.

        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var chatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);
        var incoming = new Dictionary<ChatId, Moment> {
            [chatA] = now - TimeSpan.FromSeconds(100),
            [chatB] = now - TimeSpan.FromSeconds(1),
        };
        var own = new Dictionary<ChatId, Moment> {
            [chatA] = now - TimeSpan.FromSeconds(1),
            [chatB] = now - TimeSpan.FromSeconds(100),
        };

        // act
        var merged = VoiceActivityUI.MergeSnapshots(incoming, own);

        // assert
        merged[chatA].Should().Be(now - TimeSpan.FromSeconds(1), "you spoke last in A");
        merged[chatB].Should().Be(now - TimeSpan.FromSeconds(1), "they spoke last in B");
    }

    [Fact]
    public void AnUnheardReplyShouldNotExtendTheOwnVoiceWindow()
    {
        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);
        var stamps = new Dictionary<ChatId, Moment>();

        // act
        var hasStamped = VoiceActivityUI.ApplyOwnVoiceEnd(stamps, chatA, isUnheard: true, now);

        // assert
        hasStamped.Should().BeFalse();
        stamps.Should().BeEmpty("a reply that heard nothing must not arm the triggers that opened it");
    }

    [Fact]
    public void AnUnheardReplyShouldNotCutShortAnEarlierWindow()
    {
        // The false-gesture case: you speak, which opens a window; a stray gesture inside it
        // opens a mic that hears nothing. Suppressing that close must leave your stamp alone -
        // clearing it would end a window a real utterance opened.

        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);
        var spokeAt = now - TimeSpan.FromSeconds(5);
        var stamps = new Dictionary<ChatId, Moment> { [chatA] = spokeAt };

        // act
        var hasStamped = VoiceActivityUI.ApplyOwnVoiceEnd(stamps, chatA, isUnheard: true, now);

        // assert
        hasStamped.Should().BeFalse();
        stamps[chatA].Should().Be(spokeAt, "the earlier utterance still owns its window");
    }

    [Fact]
    public void AHeardReplyShouldStampItsEnd()
    {
        // arrange
        var chatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
        var now = Moment.EpochStart + TimeSpan.FromDays(20_000);
        var stamps = new Dictionary<ChatId, Moment> { [chatA] = now - TimeSpan.FromSeconds(30) };

        // act
        var hasStamped = VoiceActivityUI.ApplyOwnVoiceEnd(stamps, chatA, isUnheard: false, now);

        // assert
        hasStamped.Should().BeTrue();
        stamps[chatA].Should().Be(now, "the window runs from the end of what you just said");
    }
}
