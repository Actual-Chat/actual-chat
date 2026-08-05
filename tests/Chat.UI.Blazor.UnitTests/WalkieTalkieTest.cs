using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class WalkieTalkieTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    [Fact]
    public void FreshWakeIsNotStale()
    {
        // act + assert
        WalkieTalkie.IsStaleWake(T0, T0 + TimeSpan.FromSeconds(3)).Should().BeFalse();
        WalkieTalkie.IsStaleWake(T0, T0 + Constants.Audio.WalkieTalkieStaleWakeAge).Should().BeFalse();
    }

    [Fact]
    public void OldWakeIsStale()
    {
        // act + assert
        WalkieTalkie.IsStaleWake(T0, T0 + Constants.Audio.WalkieTalkieStaleWakeAge + TimeSpan.FromSeconds(1))
            .Should().BeTrue();
    }

    [Fact]
    public void OngoingStreamingYieldsNoDropTime()
    {
        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(
            hasAnyActivity: true, lastActiveAt: T0, T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().BeNull();
    }

    [Fact]
    public void DropTimeIsIdleTimeoutAfterLatestActivity()
    {
        // arrange
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActiveAt = T0 + TimeSpan.FromMinutes(2);

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(hasAnyActivity: false, lastActiveAt, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(2) + idleTimeout);
    }

    [Fact]
    public void IdleSinceClampsStaleActivityTimes()
    {
        // arrange: a stamp leaked from a prior session must not shorten the idle window
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActiveAt = T0 - TimeSpan.FromHours(2);

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(hasAnyActivity: false, lastActiveAt, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + idleTimeout);
    }

    [Fact]
    public void NoActivityTimesFallBackToIdleSince()
    {
        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(
            hasAnyActivity: false, lastActiveAt: null, T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void PracticeModeBlocksTransmit()
    {
        // act + assert
        WalkieTalkie.MayTransmit(isPracticeMode: true, recordingChatId: null).Should().BeFalse();
    }

    [Fact]
    public void OpenMicBlocksTransmit()
    {
        // act + assert
        WalkieTalkie.MayTransmit(isPracticeMode: false, ChatId.Parse("testchatid1234567890"))
            .Should().BeFalse();
    }

    [Fact]
    public void IdleNonPracticeStateAllowsTransmit()
    {
        // act + assert
        WalkieTalkie.MayTransmit(isPracticeMode: false, recordingChatId: null).Should().BeTrue();
    }
}
