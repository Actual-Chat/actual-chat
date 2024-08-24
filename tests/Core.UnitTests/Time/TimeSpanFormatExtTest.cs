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
}
