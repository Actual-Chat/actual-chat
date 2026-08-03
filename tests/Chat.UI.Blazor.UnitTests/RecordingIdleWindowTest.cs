using ActualChat.Audio;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class RecordingIdleWindowTest
{
    private static readonly AudioSettings Settings = new();

    [Fact]
    public void NullIdleDurationKeepsDefaults()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(null, Settings);
        options.IdleTimeout.Should().Be(Constants.Audio.RecordingDuration);
        options.PreCountdownTimeout.Should().Be(Settings.IdleRecordingPreCountdownTimeout);
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void CustomIdleDurationShiftsPreCountdownWithIt()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(120), Settings);
        options.IdleTimeout.Should().Be(TimeSpan.FromSeconds(120));
        // The countdown cue must still start 10s before the close, as it does at the default 30s.
        options.PreCountdownTimeout.Should().Be(TimeSpan.FromSeconds(110));
        options.CheckPeriod.Should().Be(Settings.IdleRecordingCheckPeriod);
    }

    [Fact]
    public void ShortIdleDurationNeverYieldsNegativePreCountdown()
    {
        var options = ChatAudioUI.GetRecordingIdleOptions(TimeSpan.FromSeconds(5), Settings);
        options.PreCountdownTimeout.Should().Be(TimeSpan.Zero);
    }
}
