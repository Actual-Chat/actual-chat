using ActualChat.Audio;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class RecordingIdleWindowTest
{
    private static readonly AudioSettings Settings = new();
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly ChatAudioUI.RecordingIdleOptions Options =
        ChatAudioUI.GetRecordingIdleOptions(null, Settings);

    [Fact]
    public void NullIdleDurationShouldKeepDefaults()
    {
        // act
        var options = ChatAudioUI.GetRecordingIdleOptions(null, Settings);

        // assert
        options.IdleTimeout.Should().Be(Constants.Audio.RecordingDuration);
        options.PreCountdownTimeout.Should().Be(Settings.IdleRecordingPreCountdownTimeout);
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void CustomIdleDurationShouldShiftPreCountdownWithIt()
    {
        // act
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(120), Settings);

        // assert
        options.IdleTimeout.Should().Be(TimeSpan.FromSeconds(120));
        // The countdown cue must still start 10s before the close, as it does at the default 30s.
        options.PreCountdownTimeout.Should().Be(TimeSpan.FromSeconds(110));
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void ShortIdleDurationShouldNeverYieldNegativePreCountdown()
    {
        // act
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(5), Settings);

        // assert
        options.PreCountdownTimeout.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ActivityEdgeShouldStartTheFullIdleWindow()
    {
        // act
        var step = ChatAudioUI.GetRecordingIdleStep(T0, T0, Options);

        // assert
        step.MustStop.Should().BeFalse();
        step.StopAt.Should().BeNull();
        step.Wait.Should().Be(Options.PreCountdownTimeout,
            "the watcher must sleep out the whole pre-countdown rather than re-sample activity");
    }

    [Fact]
    public void CountdownShouldStartAtPreCountdownTimeout()
    {
        // act
        var justBefore = ChatAudioUI.GetRecordingIdleStep(
            T0, T0 + Options.PreCountdownTimeout - TimeSpan.FromSeconds(1), Options);
        var atStart = ChatAudioUI.GetRecordingIdleStep(T0, T0 + Options.PreCountdownTimeout, Options);

        // assert
        justBefore.StopAt.Should().BeNull();
        justBefore.Wait.Should().Be(TimeSpan.FromSeconds(1));
        atStart.StopAt.Should().Be(T0 + Options.IdleTimeout);
        atStart.Wait.Should().Be(Options.CheckPeriod);
    }

    [Fact]
    public void CountdownShouldBeVisibleForTheWholeLeadTime()
    {
        // act
        var atStart = ChatAudioUI.GetRecordingIdleStep(T0, T0 + Options.PreCountdownTimeout, Options);

        // assert
        (atStart.StopAt!.Value - (T0 + Options.PreCountdownTimeout))
            .Should().Be(Options.IdleTimeout - Options.PreCountdownTimeout,
                "the countdown is anchored to the activity edge, so it can never be cut short");
    }

    [Fact]
    public void CountdownTailShouldNotOvershootTheStop()
    {
        // act
        var step = ChatAudioUI.GetRecordingIdleStep(
            T0, T0 + Options.IdleTimeout - TimeSpan.FromSeconds(0.5), Options);

        // assert
        step.StopAt.Should().Be(T0 + Options.IdleTimeout);
        step.Wait.Should().Be(TimeSpan.FromSeconds(0.5),
            "the last wait is clamped to what's left, not to the check period");
    }

    [Fact]
    public void ExpiredIdleTimeoutShouldStop()
    {
        // act
        var atTimeout = ChatAudioUI.GetRecordingIdleStep(T0, T0 + Options.IdleTimeout, Options);
        var pastTimeout = ChatAudioUI.GetRecordingIdleStep(
            T0, T0 + Options.IdleTimeout + TimeSpan.FromSeconds(5), Options);

        // assert
        atTimeout.MustStop.Should().BeTrue();
        pastTimeout.MustStop.Should().BeTrue();
    }
}
