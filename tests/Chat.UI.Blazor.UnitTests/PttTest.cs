using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class PttTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    [Fact]
    public void FreshWakeIsNotStale()
    {
        // act + assert
        Ptt.IsStaleWake(T0, T0 + TimeSpan.FromSeconds(3)).Should().BeFalse();
        Ptt.IsStaleWake(T0, T0 + Constants.Audio.PttStaleWakeAge).Should().BeFalse();
    }

    [Fact]
    public void OldWakeIsStale()
    {
        // act + assert
        Ptt.IsStaleWake(T0, T0 + Constants.Audio.PttStaleWakeAge + TimeSpan.FromSeconds(1))
            .Should().BeTrue();
    }

    [Fact]
    public void OngoingStreamingYieldsNoDropTime()
    {
        // act
        var dropAt = Ptt.ComputeIdleDropAt(
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
        var dropAt = Ptt.ComputeIdleDropAt(hasAnyActivity: false, lastActiveAt, T0, idleTimeout);

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
        var dropAt = Ptt.ComputeIdleDropAt(hasAnyActivity: false, lastActiveAt, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + idleTimeout);
    }

    [Fact]
    public void NoActivityTimesFallBackToIdleSince()
    {
        // act
        var dropAt = Ptt.ComputeIdleDropAt(
            hasAnyActivity: false, lastActiveAt: null, T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void PracticeModeBlocksTransmit()
    {
        // act + assert
        Ptt.MayTransmit(isPracticeMode: true, recordingChatId: null).Should().BeFalse();
    }

    [Fact]
    public void OpenMicBlocksTransmit()
    {
        // act + assert
        Ptt.MayTransmit(isPracticeMode: false, ChatId.Parse("testchatid1234567890"))
            .Should().BeFalse();
    }

    [Fact]
    public void IdleNonPracticeStateAllowsTransmit()
    {
        // act + assert
        Ptt.MayTransmit(isPracticeMode: false, recordingChatId: null).Should().BeTrue();
    }

    [Fact]
    public void CapturedAudioIsNeverAMicFailure()
    {
        // act + assert
        PttReplyUI
            .ShouldReportMicFailure(hasSignal: true, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(4))
            .Should().BeFalse();
    }

    [Fact]
    public void SilenceBeforeTheDeadlineIsNotYetAMicFailure()
    {
        // act + assert
        PttReplyUI
            .ShouldReportMicFailure(hasSignal: false, TimeSpan.FromSeconds(3.9), TimeSpan.FromSeconds(4))
            .Should().BeFalse();
    }

    [Fact]
    public void SilencePastTheDeadlineIsAMicFailure()
    {
        // The recorder reports itself started as soon as AudioRecord initializes, so "no captured
        // samples" is the only honest signal that the microphone never actually opened.
        // act + assert
        PttReplyUI
            .ShouldReportMicFailure(hasSignal: false, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4))
            .Should().BeTrue();
    }
}
