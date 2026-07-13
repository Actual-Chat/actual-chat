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
        // arrange: null last-activity means someone is streaming right now
        var lastActivityTimes = new List<Moment?> { T0, null };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().BeNull();
    }

    [Fact]
    public void DropTimeIsIdleTimeoutAfterLatestActivity()
    {
        // arrange
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActivityTimes = new List<Moment?> { T0 + TimeSpan.FromMinutes(1), T0 + TimeSpan.FromMinutes(2) };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(2) + idleTimeout);
    }

    [Fact]
    public void IdleSinceClampsStaleActivityTimes()
    {
        // arrange: cached activity from a prior session must not shorten the idle window
        var idleTimeout = TimeSpan.FromMinutes(5);
        var lastActivityTimes = new List<Moment?> { T0 - TimeSpan.FromHours(2) };

        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt(lastActivityTimes, T0, idleTimeout);

        // assert
        dropAt.Should().Be(T0 + idleTimeout);
    }

    [Fact]
    public void NoActivityTimesFallBackToIdleSince()
    {
        // act
        var dropAt = WalkieTalkie.ComputeIdleDropAt([], T0, TimeSpan.FromMinutes(5));

        // assert
        dropAt.Should().Be(T0 + TimeSpan.FromMinutes(5));
    }
}
