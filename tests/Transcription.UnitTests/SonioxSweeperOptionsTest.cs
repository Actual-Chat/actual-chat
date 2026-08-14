namespace ActualChat.Transcription.UnitTests;

public class SonioxSweeperOptionsTest
{
    private static readonly SonioxSweeper.Options Settings = new();

    [Fact]
    public void PeriodIsFourHoursPlusMinusQuarter()
    {
        // assert
        Settings.Period.Origin.Should().Be(TimeSpan.FromHours(4));
        Settings.Period.Min.Should().Be(TimeSpan.FromHours(3));
        Settings.Period.Max.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public void FirstDelaySpansTheWholePeriod()
    {
        // A host that starts must land anywhere in the period, so N hosts spread out
        Settings.FirstDelay.Min.Should().Be(TimeSpan.Zero);
        Settings.FirstDelay.Max.Should().Be(TimeSpan.FromHours(4));
    }

    [Fact]
    public void DrawnDelaysStayInRangeAndVary()
    {
        // act
        var firstDelays = Enumerable.Range(0, 200).Select(_ => Settings.FirstDelay.Next()).ToList();
        var periods = Enumerable.Range(0, 200).Select(_ => Settings.Period.Next()).ToList();

        // assert
        firstDelays.Should().OnlyContain(x => x >= TimeSpan.Zero && x <= TimeSpan.FromHours(4));
        periods.Should().OnlyContain(x => x >= TimeSpan.FromHours(3) && x <= TimeSpan.FromHours(5));
        firstDelays.Distinct().Should().HaveCountGreaterThan(100);
        periods.Distinct().Should().HaveCountGreaterThan(100);
    }

    [Fact]
    public void RetentionOutlivesTheLongestTranscription()
        // A file still being transcribed on another host is indistinguishable from an orphan
        => Settings.Retention.Should().BeGreaterThanOrEqualTo(Constants.Audio.MaxStreamDuration);
}
