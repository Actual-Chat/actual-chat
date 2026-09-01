using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class SensorFeedTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static Moment At(double s) => T0 + TimeSpan.FromSeconds(s);

    [Fact]
    public void JustStartedWithoutSamplesShouldNotBeStale()
        => SensorFeed.IsAccelerometerStale(startedAt: T0, lastSampleAt: default, now: At(1), Timeout)
            .Should().BeFalse("the sensor is still warming up");

    [Fact]
    public void StartedLongAgoWithoutSamplesShouldBeStale()
        => SensorFeed.IsAccelerometerStale(startedAt: T0, lastSampleAt: default, now: At(5), Timeout)
            .Should().BeTrue("a registration that never delivered is dead");

    [Fact]
    public void RecentSamplesShouldNotBeStale()
        => SensorFeed.IsAccelerometerStale(startedAt: T0, lastSampleAt: At(9), now: At(10), Timeout)
            .Should().BeFalse();

    [Fact]
    public void SamplesStoppedPastTimeoutShouldBeStale()
        => SensorFeed.IsAccelerometerStale(startedAt: T0, lastSampleAt: At(4), now: At(10), Timeout)
            .Should().BeTrue("delivery stopped even though the flag still says on");

    [Fact]
    public void SamplesShouldOutweighAnOldStart()
        => SensorFeed.IsAccelerometerStale(startedAt: T0, lastSampleAt: At(100), now: At(101), Timeout)
            .Should().BeFalse();
}
