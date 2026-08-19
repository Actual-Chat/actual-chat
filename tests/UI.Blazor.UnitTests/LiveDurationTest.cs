using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class LiveDurationTest
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(599, "9:59")]
    [InlineData(600, "10:00")]
    [InlineData(3599, "59:59")]
    public void ShorterThanAnHourShowsMinutesAndSeconds(int seconds, string expected)
        => LiveDuration.Format(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    [InlineData(86399, "23:59:59")]
    public void AnHourAndLongerGainsTheHoursPart(int seconds, string expected)
        => LiveDuration.Format(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void ClockSkewReadsAsZeroRatherThanCountingDown(int seconds)
        => LiveDuration.Format(TimeSpan.FromSeconds(seconds)).Should().Be("0:00");

    [Fact]
    public void SubSecondPartIsTruncatedNotRounded()
        => LiveDuration.Format(TimeSpan.FromMilliseconds(1900)).Should().Be("0:01");
}
