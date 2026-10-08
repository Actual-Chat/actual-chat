using ActualChat.Time;

namespace ActualChat.Core.UnitTests.Time;

public class TimeSpanFormatExtTest
{
    [Fact]
    public void FormatTest()
    {
        var ts = TimeSpan.FromSeconds(1.1);
        ts.Format("Default").Should().Be("1s");
        ts.Format("Short").Should().Be("1.1s");
        ts.Format("ss").Should().Be("01");

        ts = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30);
        ts.Format("Default").Should().Be("1:30");
        ts.Format("Short").Should().Be("1m 30s");
        ts.Format("mm\\:ss").Should().Be("01:30");

        ts = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(30);
        ts.Format("Default").Should().Be("10:30");
        ts.Format("Short").Should().Be("10m 30s");
        ts.Format("mm\\:ss").Should().Be("10:30");

        ts = TimeSpan.FromHours(25) + TimeSpan.FromSeconds(30);
        ts.Format("Default").Should().Be("1 day, 1:00:30");
        ts.Format("Short").Should().Be("25h 0m 30s");
        ts.Format("d\\d\\ hh\\:mm\\:ss").Should().Be("1d 01:00:30");

        ts = TimeSpan.FromHours(49) + TimeSpan.FromSeconds(30);
        ts.Format("Default").Should().Be("2 days, 1:00:30");
        ts.Format("Short").Should().Be("49h 0m 30s");
        ts.Format("d\\d\\ hh\\:mm\\:ss").Should().Be("2d 01:00:30");
    }

    [Theory]
    [InlineData(0, 0, PeriodUnit.Minutes)]
    [InlineData(5, 5, PeriodUnit.Minutes)]
    [InlineData(90, 90, PeriodUnit.Minutes)]
    [InlineData(120, 2, PeriodUnit.Hours)]
    [InlineData(60 * 36, 36, PeriodUnit.Hours)]
    [InlineData(60 * 48, 2, PeriodUnit.Days)]
    public void ToPeriodUnitsShouldPickTheLargestUnitThatDivides(int minutes, int count, PeriodUnit unit)
    {
        // act
        var result = TimeSpan.FromMinutes(minutes).ToPeriodUnits();

        // assert
        result.Should().Be((count, unit));
        if (count != 0)
            unit.ToTimeSpan(count).Should().Be(TimeSpan.FromMinutes(minutes));
    }
}
