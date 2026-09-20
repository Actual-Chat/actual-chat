using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ReplayClockTest
{
    private static readonly TimeSpan MaxExtrapolation = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void AnIdleClockShouldRunOnWallTime()
    {
        // arrange
        var clock = new ReplayClock(Seconds(10), MaxExtrapolation);

        // act & assert
        clock.GetPosition(Seconds(10)).Should().Be(TimeSpan.Zero);
        clock.GetPosition(Seconds(12.5)).Should().Be(Seconds(2.5));
    }

    [Fact]
    public void ATrackThatHasntStartedSoundingShouldHoldTheClock()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);

        // act
        clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);

        // assert
        clock.GetPosition(Seconds(3)).Should().Be(TimeSpan.Zero, "the track is still buffering");
    }

    [Fact]
    public void TheClockShouldFollowTheTracksProgress()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var track = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);

        // act - the track starts sounding 3 s late
        clock.ReportProgress(track, Seconds(0.2), false, Seconds(3.2));

        // assert
        clock.GetPosition(Seconds(3.2)).Should().Be(Seconds(0.2));
        clock.GetPosition(Seconds(3.3)).Should().Be(Seconds(0.3), "the position is extrapolated between reports");
        clock.GetPosition(Seconds(5)).Should().Be(Seconds(0.5), "a report that doesn't come means starving");
    }

    [Fact]
    public void ThePositionShouldBeTheMostBehindTrack()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var first = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);
        clock.ReportProgress(first, Seconds(2), false, Seconds(2));
        var second = clock.StartTrack(Seconds(2), Seconds(2));

        // act
        clock.ReportProgress(first, Seconds(4), false, Seconds(4));
        clock.ReportProgress(second, Seconds(1), false, Seconds(4));

        // assert
        clock.GetPosition(Seconds(4)).Should().Be(Seconds(3), "the second track started a second late");
    }

    [Fact]
    public void TheClockShouldNeverStepBack()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        clock.GetPosition(Seconds(1)).Should().Be(Seconds(1));

        // act - a track due at 0.9 s is started a bit after its time
        clock.StartTrack(Seconds(0.9), Seconds(1));

        // assert
        clock.GetPosition(Seconds(1.5)).Should().Be(Seconds(1), "it holds until the track catches up");
    }

    [Fact]
    public void APausedTrackShouldNotBeExtrapolated()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var track = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);
        clock.ReportProgress(track, Seconds(1), false, Seconds(1));
        clock.GetPosition(Seconds(1.1)).Should().Be(Seconds(1.1));

        // act
        clock.ReportProgress(track, Seconds(1), true, Seconds(1.1));

        // assert
        clock.GetPosition(Seconds(1.2)).Should().Be(Seconds(1.1), "the clock never steps back to the reported position");
        clock.GetPosition(Seconds(10)).Should().Be(Seconds(1.1), "a paused track isn't extrapolated");
    }

    [Fact]
    public void TheClockShouldRunOnWallTimeAgainOnceTheLastTrackEnds()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var track = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);
        clock.ReportProgress(track, Seconds(2), false, Seconds(5));

        // act
        clock.EndTrack(track, Seconds(5));

        // assert
        clock.GetPosition(Seconds(5.4)).Should().Be(Seconds(2.4));
    }

    [Fact]
    public void WhenReachedShouldCompleteOnlyOnceTheAudioCarriesThePositionThere()
    {
        // arrange
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var track = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);

        // act
        var whenReached = clock.WhenReached(Seconds(2), TimeSpan.Zero);

        // assert
        clock.GetPosition(Seconds(9)).Should().Be(TimeSpan.Zero, "the track is still buffering");
        whenReached.IsCompleted.Should().BeFalse("wall time alone doesn't move a clock a track holds");
        clock.ReportProgress(track, Seconds(2), false, Seconds(9));
        whenReached.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void WhenReachedShouldCompleteWhenTheTrackHoldingTheClockEnds()
    {
        // arrange - one track lags at its start while another is already 2 s in
        var clock = new ReplayClock(TimeSpan.Zero, MaxExtrapolation);
        var lagging = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);
        var playing = clock.StartTrack(TimeSpan.Zero, TimeSpan.Zero);
        clock.ReportProgress(playing, Seconds(2), false, Seconds(2));
        var whenReached = clock.WhenReached(Seconds(1.5), Seconds(2));
        whenReached.IsCompleted.Should().BeFalse("the lagging track holds the clock at its start");

        // act
        clock.EndTrack(lagging, Seconds(2));

        // assert
        whenReached.IsCompleted.Should().BeTrue("the clock jumps to the track that is still playing");
    }

    // Private methods

    private static TimeSpan Seconds(double value)
        => TimeSpan.FromSeconds(value);
}
